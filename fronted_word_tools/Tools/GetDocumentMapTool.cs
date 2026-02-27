using FuXing.SubAgents;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 获取文档 Map（AST 树形结构图），彻底替代原 GetDocumentOutlineTool。
    ///
    /// 核心升级：
    /// - 原工具只能读取标准 Heading 样式，不规范文档直接失效
    /// - 新工具对不规范文档自动调用 LLM 推断标题层级映射规则
    /// - 返回树形结构（类似编码智能体的 Repo Map），而非平面列表
    /// - 每个节点有唯一 Hash ID，配合 get_node_detail 按需深入
    /// - 内置缓存，文档未变更时 O(1) 返回
    /// </summary>
    public class GetDocumentMapTool : ToolBase
    {
        public override string Name => "get_document_map";
        public override string DisplayName => "获取文档结构图";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Get the document's tree-structured Document Map. " +
            "Returns a hierarchical section tree where each node has a unique ID; use with get_node_detail to view specific section content. " +
            "For documents without heading styles, automatically uses AI to infer heading levels from paragraph formatting. " +
            "Results are cached and returned instantly when document content is unchanged.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
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
            bool forceRebuild = OptionalBool(arguments, "force_rebuild", false);

            if (forceRebuild)
                DocumentMapCache.Instance.Invalidate(doc.FullName);

            var map = await DocumentMapCache.Instance.GetOrBuildAsync(doc);
            return ToolExecutionResult.Ok(map.ToMapText());
        }
    }
}
