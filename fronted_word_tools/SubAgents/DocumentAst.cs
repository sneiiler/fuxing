using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  文档 AST 节点模型（内部使用）
    //
    //  仅被 DocumentGraphBuilder 深度路径引用，用于 LLM 推断标题层级。
    //  外部应使用 DocumentGraph (Core 命名空间) 作为文档模型。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>AST 节点类型</summary>
    public enum AstNodeType
    {
        /// <summary>文档根节点</summary>
        Root,

        /// <summary>章节节点（标题 + 其下内容）</summary>
        Section,
    }

    /// <summary>
    /// 文档 AST 节点（内部模型）。
    /// 仅用于 DocumentAstBuilder 的 LLM 推断流程，外部请使用 DocNode。
    /// </summary>
    public class DocumentAstNode
    {
        /// <summary>节点 ID（内容 hash 前 8 位），仅内部使用</summary>
        public string NodeId { get; set; }

        /// <summary>节点类型</summary>
        public AstNodeType Type { get; set; }

        /// <summary>标题级别（1-6 for Section, 0 for Root）</summary>
        public int Level { get; set; }

        /// <summary>标题文本（Section 节点）/ 文档名（Root 节点）</summary>
        public string Title { get; set; }

        /// <summary>段落索引范围起始（1-based）</summary>
        public int ParaStart { get; set; }

        /// <summary>段落索引范围结束（1-based）</summary>
        public int ParaEnd { get; set; }

        /// <summary>文档字符偏移起始</summary>
        public int CharStart { get; set; }

        /// <summary>文档字符偏移结束（-1 表示到文档末尾）</summary>
        public int CharEnd { get; set; }

        /// <summary>该节点下的总段落数（含子节点）</summary>
        public int TotalParaCount { get; set; }

        /// <summary>该节点本身直接包含的段落数</summary>
        public int DirectParaCount { get; set; }

        /// <summary>首段正文的内容预览</summary>
        public string ContentPreview { get; set; }

        /// <summary>子节点列表</summary>
        public List<DocumentAstNode> Children { get; set; } = new List<DocumentAstNode>();

        /// <summary>
        /// 生成节点 ID：基于 level + title + paraIndex 的 MD5 前 8 位。
        /// </summary>
        public static string ComputeNodeId(int level, string title, int paraIndex)
        {
            string input = $"{level}:{title}:{paraIndex}";
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
