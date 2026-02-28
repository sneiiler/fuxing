using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 获取文档 Map（AST 树形结构图）。
    ///
    /// 双模式设计：
    /// - 默认快速感知：仅读取具有大纲级别的标题，程序化构建 AST，无 LLM 调用
    /// - 深度感知（deep=true）：提取全量段落格式，LLM 推断标题层级，适用于不规范文档
    ///
    /// 快速感知若检测到结构不完整，会在结果中提示可启用深度感知。
    /// </summary>
    public class GetDocumentMapTool : ToolBase
    {
        public override string Name => "get_document_map";
        public override string DisplayName => "获取文档结构图";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Get hierarchical Document Map (section tree with unique node IDs). " +
            "Default: fast mode reads only outline-level headings (no LLM). " +
            "Set deep=true for AI-powered heading inference on unformatted documents. " +
            "Use node IDs with read_document_section to read content. Cached.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["deep"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "启用深度感知：分析所有段落的字体格式，由 AI 推断标题层级。" +
                                     "适用于未使用标准标题样式的文档。默认 false"
                },
                ["force_rebuild"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "强制重新构建（忽略缓存）。默认 false"
                }
            }
        };

        public override async Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            bool deep = OptionalBool(arguments, "deep", false);
            bool forceRebuild = OptionalBool(arguments, "force_rebuild", false);

            if (forceRebuild)
                DocumentMapCache.Instance.Invalidate(doc.FullName);

            var map = await DocumentMapCache.Instance.GetOrBuildAsync(doc, deep);
            return ToolExecutionResult.Ok(map.ToMapText());
        }
    }
}
