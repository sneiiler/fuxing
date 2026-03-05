using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FuXingAgent.Core
{
    /// <summary>文档图节点类型</summary>
    public enum DocNodeType
    {
        Document,
        Section,
        Heading,
        Preamble,
        Table,
        Image,
        TextBlock,
        List,
        Paragraph,
    }

    /// <summary>文档图节点</summary>
    public class DocNode
    {
        public string Id { get; set; }
        public DocNodeType Type { get; set; }
        public string Title { get; set; }
        public string Preview { get; set; }
        public int Level { get; set; }
        public string ParentId { get; set; }
        public List<string> ChildIds { get; set; } = new List<string>();
        public string PrevId { get; set; }
        public string NextId { get; set; }
        public bool Expanded { get; set; }
        public string Label { get; set; }
        public Dictionary<string, string> Meta { get; set; }
    }

    /// <summary>文档图：节点索引 + 类型索引 + 图输出</summary>
    public class DocumentGraph
    {
        public string DocumentName { get; set; }
        public int ContentHash { get; set; }
        public DocNode Root { get; set; }

        public Dictionary<string, DocNode> Index { get; set; }
            = new Dictionary<string, DocNode>();

        public Dictionary<DocNodeType, List<DocNode>> TypeIndex { get; set; }
            = new Dictionary<DocNodeType, List<DocNode>>();

        public Dictionary<string, string> LabelIndex { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public bool IsDeepPerception { get; set; }
        public System.DateTime BuiltAt { get; set; }

        public DocNode GetById(string id)
        {
            Index.TryGetValue(id, out var node);
            return node;
        }

        public DocNode ResolveNode(string idOrLabel)
        {
            if (string.IsNullOrWhiteSpace(idOrLabel)) return null;
            if (Index.TryGetValue(idOrLabel, out var node)) return node;
            if (LabelIndex.TryGetValue(idOrLabel, out var resolvedId))
                return GetById(resolvedId);
            return null;
        }

        public void SetLabel(string nodeId, string label)
        {
            var node = GetById(nodeId);
            if (node == null)
                throw new System.InvalidOperationException($"节点不存在: {nodeId}");

            if (!string.IsNullOrEmpty(node.Label))
                LabelIndex.Remove(node.Label);

            if (!string.IsNullOrEmpty(label))
            {
                if (LabelIndex.TryGetValue(label, out var existingId) && existingId != nodeId)
                {
                    var existingNode = GetById(existingId);
                    if (existingNode != null) existingNode.Label = null;
                }
                LabelIndex[label] = nodeId;
            }

            node.Label = label;
        }

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

        public DocNode FindNodeAtPosition(int position)
        {
            DocNode best = null;
            int bestSize = int.MaxValue;

            foreach (var node in Index.Values)
            {
                if (node.Type == DocNodeType.Document) continue;
                if (node.Meta == null) continue;
                if (!node.Meta.TryGetValue("range_end", out var re)) continue;

                int start;
                if (node.Meta.TryGetValue("heading_start", out var hs))
                    start = int.Parse(hs);
                else if (node.Meta.TryGetValue("range_start", out var rs))
                    start = int.Parse(rs);
                else
                    continue;

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

        public List<DocNode> FindByType(DocNodeType type)
        {
            if (TypeIndex.TryGetValue(type, out var list))
                return list;
            return new List<DocNode>();
        }

        public DocNode Parent(string id)
        {
            var node = GetById(id);
            if (node?.ParentId == null) return null;
            return GetById(node.ParentId);
        }

        public List<DocNode> Children(string id)
        {
            var node = GetById(id);
            if (node == null) return new List<DocNode>();
            return node.ChildIds.Select(cid => GetById(cid))
                       .Where(n => n != null).ToList();
        }

        public void AddNode(DocNode node)
        {
            Index[node.Id] = node;
            if (!TypeIndex.ContainsKey(node.Type))
                TypeIndex[node.Type] = new List<DocNode>();
            TypeIndex[node.Type].Add(node);
        }

        public void RemoveNode(string id)
        {
            if (!Index.TryGetValue(id, out var node)) return;
            if (!string.IsNullOrEmpty(node.Label))
                LabelIndex.Remove(node.Label);
            Index.Remove(id);
            if (TypeIndex.TryGetValue(node.Type, out var list))
                list.RemoveAll(n => n.Id == id);
        }

        public string ToGraphText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Document Graph: {DocumentName}");
            sb.AppendLine($"节点数: {Index.Count} | 感知: {(IsDeepPerception ? "深度" : "快速")}");
            sb.AppendLine();

            if (Root.ChildIds.Count == 0)
            {
                sb.AppendLine("（未检测到任何文档结构）");
                return sb.ToString();
            }

            foreach (var childId in Root.ChildIds)
                AppendNode(sb, childId, 0);

            return sb.ToString();
        }

        private void AppendNode(StringBuilder sb, string nodeId, int indent)
        {
            var node = GetById(nodeId);
            if (node == null) return;

            string pad = new string(' ', indent * 2);
            string preview = !string.IsNullOrEmpty(node.Preview)
                ? $" \"{Truncate(node.Preview, 40)}\""
                : "";

            string levelStr = node.Type == DocNodeType.Section
                ? $"§{node.Level} "
                : "";

            sb.AppendLine($"{pad}[{node.Id}] {levelStr}{node.Title}{preview}");

            foreach (var childId in node.ChildIds)
                AppendNode(sb, childId, indent + 1);
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }
    }
}
