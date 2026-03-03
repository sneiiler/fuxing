using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FuXing.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  文档图（Document Graph）模型  ——  “万物皆节点”
    //
    //  每个可寻址的文档元素都是图中的 DocNode，由 CC 锚定。
    //  三层粒度，逐层 expand：
    //  - L1 骨架层：Section 节点（标题），map 时自动创建
    //  - L2 内容层：Table / Image / TextBlock / List，expand(section) 时创建
    //  - L3 段落层：Paragraph，expand(textblock) 时创建
    //
    //  节点可被 AI 赋予 label 别名，用于多步操作中的稳定引用。
    //  所有编辑工具统一接受 node_id（或 label）定位。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>文档图节点类型</summary>
    public enum DocNodeType
    {
        /// <summary>根节点，代表整个文档</summary>
        Document,

        /// <summary>标题章节（标题 + 其直属内容区域）</summary>
        Section,

        /// <summary>表格</summary>
        Table,

        /// <summary>图片</summary>
        Image,

        /// <summary>连续段落块（非标题、非表格、非图片的正文文本）</summary>
        TextBlock,

        /// <summary>列表块（连续的列表项段落）</summary>
        List,

        /// <summary>单个段落（L3 粒度，expand TextBlock 时创建）</summary>
        Paragraph,
    }

    /// <summary>
    /// 文档图节点。
    /// 每个节点与一个 ContentControl 绑定，通过 AnchorLabel 引用。
    /// 编辑文档时 CC 自动跟踪位置，无需重建索引。
    /// </summary>
    public class DocNode
    {
        /// <summary>简短唯一 ID: "s01", "t03", "i02", "b05", "l01"</summary>
        public string Id { get; set; }

        /// <summary>节点类型</summary>
        public DocNodeType Type { get; set; }

        // ── 内容描述 ──

        /// <summary>
        /// Section→标题文本, Table→"表N (R×C)", Image→"图N (W×H)",
        /// TextBlock→"文本块 (N段)", List→"列表 (N项)"
        /// </summary>
        public string Title { get; set; }

        /// <summary>内容前 100 字符预览</summary>
        public string Preview { get; set; }

        /// <summary>Section 专用：标题级别 1-6，其他类型为 0</summary>
        public int Level { get; set; }

        // ── CC 锚点 ──

        /// <summary>
        /// 对应的 AnchorManager 锚点标签。
        /// 通过 AnchorManager.GetRange(doc, AnchorLabel) 获取实时位置。
        /// </summary>
        public string AnchorLabel { get; set; }

        // ── 图关系（邻接表） ──

        /// <summary>父节点 ID（Document 根节点的 ParentId 为 null）</summary>
        public string ParentId { get; set; }

        /// <summary>子节点 ID 列表（有序，按文档顺序排列）</summary>
        public List<string> ChildIds { get; set; } = new List<string>();

        /// <summary>前驱兄弟节点 ID（同一父节点下的上一个节点）</summary>
        public string PrevId { get; set; }

        /// <summary>后继兄弟节点 ID（同一父节点下的下一个节点）</summary>
        public string NextId { get; set; }

        /// <summary>L2/L3 内容是否已展开（Section 和 TextBlock 类型有意义）</summary>
        public bool Expanded { get; set; }

        /// <summary>AI 设置的别名（可选，用于多步操作中的稳定引用）</summary>
        public string Label { get; set; }

        /// <summary>元数据（表格行列数、图片尺寸等）</summary>
        public Dictionary<string, string> Meta { get; set; }
    }

    /// <summary>
    /// 文档图：节点索引 + 类型索引 + 图输出。
    /// </summary>
    public class DocumentGraph
    {
        /// <summary>文档名</summary>
        public string DocumentName { get; set; }

        /// <summary>文档内容 hash（用于缓存失效检测）</summary>
        public int ContentHash { get; set; }

        /// <summary>根节点</summary>
        public DocNode Root { get; set; }

        /// <summary>O(1) 节点索引：Id → Node</summary>
        public Dictionary<string, DocNode> Index { get; set; }
            = new Dictionary<string, DocNode>();

        /// <summary>按类型索引：Type → Node 列表</summary>
        public Dictionary<DocNodeType, List<DocNode>> TypeIndex { get; set; }
            = new Dictionary<DocNodeType, List<DocNode>>();

        /// <summary>Label 别名索引：label → nodeId</summary>
        public Dictionary<string, string> LabelIndex { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>是否为深度感知模式</summary>
        public bool IsDeepPerception { get; set; }

        /// <summary>图构建时间戳</summary>
        public System.DateTime BuiltAt { get; set; }

        // ═══════════════════════════════════════════════════
        //  查询接口
        // ═══════════════════════════════════════════════════

        /// <summary>按 ID 获取节点</summary>
        public DocNode GetById(string id)
        {
            Index.TryGetValue(id, out var node);
            return node;
        }

        /// <summary>按 ID 或 label 解析节点（统一入口）</summary>
        public DocNode ResolveNode(string idOrLabel)
        {
            if (string.IsNullOrWhiteSpace(idOrLabel)) return null;

            // 先精确查 ID
            if (Index.TryGetValue(idOrLabel, out var node))
                return node;

            // 再查 label
            if (LabelIndex.TryGetValue(idOrLabel, out var resolvedId))
                return GetById(resolvedId);

            return null;
        }

        /// <summary>给节点设置 label 别名</summary>
        public void SetLabel(string nodeId, string label)
        {
            var node = GetById(nodeId);
            if (node == null)
                throw new System.InvalidOperationException($"节点不存在: {nodeId}");

            // 清除旧 label
            if (!string.IsNullOrEmpty(node.Label))
                LabelIndex.Remove(node.Label);

            // 设置新 label
            if (!string.IsNullOrEmpty(label))
            {
                // 如果该 label 已被其他节点使用，先移除
                if (LabelIndex.TryGetValue(label, out var existingId) && existingId != nodeId)
                {
                    var existingNode = GetById(existingId);
                    if (existingNode != null) existingNode.Label = null;
                }
                LabelIndex[label] = nodeId;
            }

            node.Label = label;
        }

        /// <summary>按标题模糊查找（大小写不敏感）</summary>
        public DocNode FindByTitle(string title)
        {
            foreach (var node in Index.Values)
            {
                if (node.Title != null &&
                    node.Title.Equals(title, System.StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            return null;
        }

        /// <summary>
        /// 按字符偏移位置查找最精确（范围最小）的节点。
        /// 用于将光标/选区位置映射到图节点。
        /// </summary>
        public DocNode FindNodeAtPosition(int position)
        {
            DocNode best = null;
            int bestSize = int.MaxValue;

            foreach (var node in Index.Values)
            {
                if (node.Type == DocNodeType.Document) continue;
                if (node.Meta == null) continue;
                if (!node.Meta.TryGetValue("range_start", out var rs) ||
                    !node.Meta.TryGetValue("range_end", out var re)) continue;

                int start = int.Parse(rs);
                int end = int.Parse(re);

                if (position >= start && position <= end)
                {
                    int size = end - start;
                    if (size < bestSize)
                    {
                        best = node;
                        bestSize = size;
                    }
                }
            }

            return best;
        }

        /// <summary>按类型查找所有节点</summary>
        public List<DocNode> FindByType(DocNodeType type)
        {
            if (TypeIndex.TryGetValue(type, out var list))
                return list;
            return new List<DocNode>();
        }

        // ═══════════════════════════════════════════════════
        //  导航接口
        // ═══════════════════════════════════════════════════

        /// <summary>获取父节点</summary>
        public DocNode Parent(string id)
        {
            var node = GetById(id);
            if (node?.ParentId == null) return null;
            return GetById(node.ParentId);
        }

        /// <summary>获取子节点列表</summary>
        public List<DocNode> Children(string id)
        {
            var node = GetById(id);
            if (node == null) return new List<DocNode>();
            return node.ChildIds.Select(cid => GetById(cid))
                       .Where(n => n != null).ToList();
        }

        /// <summary>获取前驱兄弟</summary>
        public DocNode Prev(string id)
        {
            var node = GetById(id);
            if (node?.PrevId == null) return null;
            return GetById(node.PrevId);
        }

        /// <summary>获取后继兄弟</summary>
        public DocNode Next(string id)
        {
            var node = GetById(id);
            if (node?.NextId == null) return null;
            return GetById(node.NextId);
        }

        // ═══════════════════════════════════════════════════
        //  索引管理
        // ═══════════════════════════════════════════════════

        /// <summary>添加节点到索引</summary>
        public void AddNode(DocNode node)
        {
            Index[node.Id] = node;

            if (!TypeIndex.ContainsKey(node.Type))
                TypeIndex[node.Type] = new List<DocNode>();
            TypeIndex[node.Type].Add(node);
        }

        /// <summary>移除节点（从索引、类型索引和标签索引中移除）</summary>
        public void RemoveNode(string id)
        {
            if (!Index.TryGetValue(id, out var node)) return;

            // 清理标签索引
            if (!string.IsNullOrEmpty(node.Label))
                LabelIndex.Remove(node.Label);

            Index.Remove(id);
            if (TypeIndex.TryGetValue(node.Type, out var list))
                list.RemoveAll(n => n.Id == id);
        }

        // ═══════════════════════════════════════════════════
        //  文本输出（给 LLM 看）
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 生成文档图的文本表示。
        /// 树形缩进展示所有节点，附带导航提示。
        /// </summary>
        public string ToGraphText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📄 Document Graph: {DocumentName}");
            sb.AppendLine($"节点数: {Index.Count} | 感知: {(IsDeepPerception ? "深度" : "快速")}");
            sb.AppendLine();

            if (Root.ChildIds.Count == 0)
            {
                sb.AppendLine("（未检测到任何文档结构）");
                return sb.ToString();
            }

            foreach (var childId in Root.ChildIds)
                AppendNode(sb, childId, 0);

            sb.AppendLine();
            sb.AppendLine("═══ 导航指令 ═══");
            sb.AppendLine("• document_graph(read, node_id) — 读取节点内容");
            sb.AppendLine("• document_graph(expand, node_id) — 展开节点（Section→表格/图片/文本块, TextBlock→段落）");
            sb.AppendLine("• document_graph(goto, node_id) — 光标跳到节点");
            sb.AppendLine("• document_graph(label, node_id, label) — 给节点赋予别名");
            sb.AppendLine("• 编辑工具支持 node_id 或 label 参数直接定位");

            return sb.ToString();
        }

        private void AppendNode(StringBuilder sb, string nodeId, int indent)
        {
            var node = GetById(nodeId);
            if (node == null) return;

            string pad = new string(' ', indent * 2);
            string icon = GetTypeIcon(node.Type);
            string preview = !string.IsNullOrEmpty(node.Preview)
                ? $" \"{Truncate(node.Preview, 40)}\""
                : "";

            // Section 节点显示级别
            string levelStr = node.Type == DocNodeType.Section
                ? $"§{node.Level} "
                : "";

            sb.AppendLine($"{pad}[{node.Id}] {icon} {levelStr}{node.Title}{preview}");

            // 未展开的 Section/TextBlock 提示
            if ((node.Type == DocNodeType.Section || node.Type == DocNodeType.TextBlock)
                && !node.Expanded && node.ChildIds.Count == 0)
            {
                // 不显示任何子内容（等待 expand）
            }

            foreach (var childId in node.ChildIds)
                AppendNode(sb, childId, indent + 1);
        }

        private static string GetTypeIcon(DocNodeType type)
        {
            switch (type)
            {
                case DocNodeType.Section: return "§";
                case DocNodeType.Table: return "📋";
                case DocNodeType.Image: return "🖼";
                case DocNodeType.TextBlock: return "📝";
                case DocNodeType.List: return "📌";
                case DocNodeType.Paragraph: return "¶";
                default: return "•";
            }
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }
    }
}
