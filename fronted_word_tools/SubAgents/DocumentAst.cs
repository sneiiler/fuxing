using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  文档 AST（抽象语法树）模型
    //
    //  类比编码智能体的 Repo Map / AST：
    //  - 代码世界: File → Class → Method → Statement
    //  - 文档世界: Document → Chapter(L1) → Section(L2) → SubSection(L3)
    //
    //  核心设计：
    //  - 树结构显式表达标题层级关系，不再让 LLM 从平面列表"脑补"
    //  - Hash-based NodeId 保证跨构建稳定性
    //  - 结构索引（Dictionary）支持 O(1) 节点查找
    //  - 渐进式披露：先看 Map（树形大纲），再按 ID 深入具体章节
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
    /// 文档 AST 节点。
    /// 树状结构：Root → Section(L1) → Section(L2) → ...
    /// 叶子章节通过 ParaRange 关联到原始 ParagraphMeta。
    /// </summary>
    public class DocumentAstNode
    {
        /// <summary>节点 ID（内容 hash 前 8 位），用于稳定寻址</summary>
        public string NodeId { get; set; }

        /// <summary>节点类型</summary>
        public AstNodeType Type { get; set; }

        /// <summary>标题级别（1-6 for Section, 0 for Root）</summary>
        public int Level { get; set; }

        /// <summary>标题文本（Section 节点）/ 文档名（Root 节点）</summary>
        public string Title { get; set; }

        /// <summary>首段正文的内容预览（前 80 字符），用于 Map 展示</summary>
        public string ContentPreview { get; set; }

        /// <summary>段落索引范围起始（1-based，含标题段本身）</summary>
        public int ParaStart { get; set; }

        /// <summary>段落索引范围结束（1-based，不包含，即 [ParaStart, ParaEnd)）</summary>
        public int ParaEnd { get; set; }

        /// <summary>文档字符偏移起始</summary>
        public int CharStart { get; set; }

        /// <summary>文档字符偏移结束（-1 表示到文档末尾）</summary>
        public int CharEnd { get; set; }

        /// <summary>该节点本身直接包含的段落数（不含子节点内的段落）</summary>
        public int DirectParaCount { get; set; }

        /// <summary>该节点下的总段落数（含子节点）</summary>
        public int TotalParaCount { get; set; }

        /// <summary>子节点列表</summary>
        public List<DocumentAstNode> Children { get; set; } = new List<DocumentAstNode>();

        /// <summary>
        /// 生成稳定的节点 ID：基于 level + title + paraIndex 的 MD5 前 8 位。
        /// 相同内容 + 位置 → 相同 ID，确保缓存重建后 ID 不变。
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

    /// <summary>
    /// 文档 Map：包含 AST 树 + 结构索引。
    /// 整个 Map 可被缓存，通过 ContentHash 判断是否失效。
    ///
    /// 双模式：
    /// - 快速感知（IsDeepPerception=false）：从大纲级别程序化构建，无 LLM
    /// - 深度感知（IsDeepPerception=true）：含完整段落元数据 + LLM 推断
    /// </summary>
    public class DocumentMap
    {
        /// <summary>文档名</summary>
        public string DocumentName { get; set; }

        /// <summary>文档内容 hash（用于缓存失效检测，来自 doc.Content.Text.GetHashCode()）</summary>
        public int ContentHash { get; set; }

        /// <summary>文档总段落数</summary>
        public int TotalParagraphs { get; set; }

        /// <summary>AST 根节点</summary>
        public DocumentAstNode Root { get; set; }

        /// <summary>结构索引：NodeId → Node，O(1) 查找任意节点</summary>
        public Dictionary<string, DocumentAstNode> Index { get; set; }
            = new Dictionary<string, DocumentAstNode>();

        /// <summary>
        /// 原始段落元数据（仅深度感知模式填充，快速模式为 null）。
        /// 深度感知的 AST 构建基础数据源。
        /// </summary>
        public DocumentStructure RawStructure { get; set; }

        /// <summary>是否为深度感知模式（含 LLM 辅助推断）</summary>
        public bool IsDeepPerception { get; set; }

        /// <summary>Map 构建时间戳</summary>
        public DateTime BuiltAt { get; set; }

        /// <summary>
        /// 生成压缩的树形大纲文本（Document Map）。
        /// 只显示节点 ID + 标题 + 段落数 + 内容预览，不包含正文全文。
        /// 相当于编码智能体的 Repo Map。
        ///
        /// 快速模式下若未检测到标题或层级单一，附加深度感知提示。
        /// </summary>
        public string ToMapText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"═══ Document Map: {DocumentName} ═══");
            sb.AppendLine($"总段落数: {TotalParagraphs}");
            sb.AppendLine($"章节数: {Index.Count}");
            sb.AppendLine($"感知模式: {(IsDeepPerception ? "深度感知（LLM 辅助）" : "快速感知（大纲级别）")}");
            sb.AppendLine();

            if (Root.Children.Count == 0)
            {
                sb.AppendLine("（未检测到任何标题结构）");
                sb.AppendLine();
                AppendDeepPerceptionHint(sb, DeepPerceptionHintReason.NoHeadings);
                return sb.ToString();
            }

            foreach (var child in Root.Children)
                AppendNodeToMap(sb, child, 0);

            // 检查是否需要提示深度感知
            if (!IsDeepPerception)
            {
                var hintReason = EvaluateMapQuality();
                if (hintReason != DeepPerceptionHintReason.None)
                {
                    sb.AppendLine();
                    AppendDeepPerceptionHint(sb, hintReason);
                }
            }

            sb.AppendLine();
            sb.AppendLine("提示: 使用 read_document_section(node_id) 查看具体章节内容");

            return sb.ToString();
        }

        /// <summary>评估快速感知结果质量，判断是否需要提示深度感知</summary>
        private DeepPerceptionHintReason EvaluateMapQuality()
        {
            if (Root.Children.Count == 0)
                return DeepPerceptionHintReason.NoHeadings;

            // 检查标题层级是否单一（所有标题都是同一级别）
            var levels = new HashSet<int>();
            CollectLevels(Root, levels);
            if (levels.Count == 1)
                return DeepPerceptionHintReason.SingleLevel;

            return DeepPerceptionHintReason.None;
        }

        private static void CollectLevels(DocumentAstNode node, HashSet<int> levels)
        {
            if (node.Type == AstNodeType.Section)
                levels.Add(node.Level);
            foreach (var child in node.Children)
                CollectLevels(child, levels);
        }

        /// <summary>追加深度感知提示信息</summary>
        private static void AppendDeepPerceptionHint(StringBuilder sb, DeepPerceptionHintReason reason)
        {
            sb.AppendLine("═══ 深度感知提示 ═══");
            switch (reason)
            {
                case DeepPerceptionHintReason.NoHeadings:
                    sb.AppendLine("当前文档未检测到任何具有大纲级别的标题。");
                    sb.AppendLine("可能原因：文档未使用标准标题样式（标题1~6），而是通过字体格式区分标题。");
                    break;
                case DeepPerceptionHintReason.SingleLevel:
                    sb.AppendLine("当前文档所有标题均为同一层级，可能存在标题层级标注不完整。");
                    break;
            }
            sb.AppendLine("建议：通过 ask_user 工具询问用户是否启用深度感知（get_document_map(deep=true)），");
            sb.AppendLine("深度感知将分析所有段落的字体格式，由 AI 推断完整的标题层级结构。");
        }

        private static void AppendNodeToMap(StringBuilder sb, DocumentAstNode node, int indent)
        {
            string pad = new string(' ', indent * 2);
            string paraInfo = $"({node.TotalParaCount}段)";
            string preview = !string.IsNullOrEmpty(node.ContentPreview)
                ? $" | {node.ContentPreview}"
                : "";

            sb.AppendLine($"{pad}[{node.NodeId}] {node.Level}级: {node.Title} {paraInfo}{preview}");

            foreach (var child in node.Children)
                AppendNodeToMap(sb, child, indent + 1);
        }
    }

    /// <summary>深度感知提示原因</summary>
    internal enum DeepPerceptionHintReason
    {
        None,
        NoHeadings,
        SingleLevel
    }
}
