using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  文档图缓存
    //
    //  单例模式，跨工具调用共享。
    //  缓存策略：
    //  - 通过 doc.Content.Text.GetHashCode() 检测内容变更
    //  - 缓存失效时清理旧图的所有 CC 锚点，再重建
    //  - 深度图优先：如缓存已有深度版本，即使请求快速也返回深度版本
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档图缓存。单例模式，跨工具调用共享。
    /// </summary>
    public class DocumentGraphCache
    {
        private static DocumentGraphCache _instance;

        /// <summary>全局单例</summary>
        public static DocumentGraphCache Instance =>
            _instance ?? (_instance = new DocumentGraphCache());

        /// <summary>缓存存储：文档全路径 → DocumentGraph</summary>
        private readonly Dictionary<string, DocumentGraph> _cache
            = new Dictionary<string, DocumentGraph>();

        /// <summary>Builder 缓存：每个文档共享一个 Builder，保持 ID 计数器连续</summary>
        private readonly Dictionary<string, DocumentGraphBuilder> _builders
            = new Dictionary<string, DocumentGraphBuilder>();

        /// <summary>共享的 AnchorManager 实例</summary>
        private readonly AnchorManager _anchors = new AnchorManager();

        /// <summary>获取共享的 AnchorManager</summary>
        public AnchorManager Anchors => _anchors;

        /// <summary>
        /// 获取节点对应的 Word Range。优先使用 CC 锚点，锚点不存在时回退到节点元数据中存储的字符偏移。
        /// </summary>
        public NetOffice.WordApi.Range GetNodeRange(
            NetOffice.WordApi.Document doc, DocNode node)
        {
            if (!string.IsNullOrEmpty(node.AnchorLabel))
            {
                try { return _anchors.GetRange(doc, node.AnchorLabel); }
                catch { /* CC 不存在，尝试回退 */ }
            }
            if (node.Meta != null
                && node.Meta.TryGetValue("range_start", out var s)
                && node.Meta.TryGetValue("range_end", out var e))
            {
                return doc.Range(int.Parse(s), int.Parse(e));
            }
            throw new System.InvalidOperationException(
                $"无法定位节点 [{node.Id}] {node.Title}：CC 锚点丢失且无位置元数据");
        }

        /// <summary>
        /// 获取文档图。优先从缓存返回；文档内容变更时自动重建。
        /// </summary>
        public async Task<DocumentGraph> GetOrBuildAsync(
            NetOffice.WordApi.Document doc,
            bool deep = false,
            CancellationToken cancellation = default)
        {
            string docKey = doc.FullName;
            int contentHash = doc.Content.Text.GetHashCode();

            // 检查缓存
            if (_cache.TryGetValue(docKey, out var cached) && cached.ContentHash == contentHash)
            {
                if (deep && !cached.IsDeepPerception)
                {
                    Debug.WriteLine($"[GraphCache] 缓存命中但需升级到深度感知: {docKey}");
                    // 继续重建
                }
                else
                {
                    Debug.WriteLine($"[GraphCache] 缓存命中: {docKey}");
                    return cached;
                }
            }

            // 保存旧图的 label 索引（用于迁移到新图）
            Dictionary<string, string> oldLabels = null;
            if (cached != null && cached.LabelIndex.Count > 0)
                oldLabels = new Dictionary<string, string>(cached.LabelIndex);

            // 缓存失效 → 清理旧 CC 锚点
            CleanupOldAnchors(doc);

            // 构建新图（新 builder，重置计数器）
            var builder = new DocumentGraphBuilder(_anchors);
            DocumentGraph graph;

            if (deep)
            {
                Debug.WriteLine($"[GraphCache] 构建深度图: {docKey}");
                graph = await builder.BuildSkeletonDeepAsync(doc, cancellation);
            }
            else
            {
                Debug.WriteLine($"[GraphCache] 构建快速图: {docKey}");
                graph = builder.BuildSkeleton(doc);
            }

            graph.ContentHash = contentHash;

            // 迁移旧 label（只保留新图中仍存在的节点）
            if (oldLabels != null)
            {
                foreach (var kv in oldLabels)
                {
                    var node = graph.GetById(kv.Value);
                    if (node != null)
                    {
                        graph.SetLabel(kv.Value, kv.Key);
                        Debug.WriteLine($"[GraphCache] 迁移 label: {kv.Key} → {kv.Value}");
                    }
                }
            }

            _cache[docKey] = graph;
            _builders[docKey] = builder; // 缓存 builder，后续 expand 复用

            Debug.WriteLine($"[GraphCache] 图已缓存: {graph.Index.Count} 个节点");
            return graph;
        }

        /// <summary>展开节点的内部内容（Section→L2, TextBlock→L3）</summary>
        public void ExpandNode(
            NetOffice.WordApi.Document doc, string nodeId)
        {
            string docKey = doc.FullName;
            if (!_cache.TryGetValue(docKey, out var graph))
                throw new System.InvalidOperationException("请先调用 document_graph(map) 构建文档图");

            var node = graph.GetById(nodeId);
            if (node == null)
                throw new System.InvalidOperationException($"节点不存在: {nodeId}");

            // 复用缓存的 builder（ID 计数器不重置，保持连续）
            if (!_builders.TryGetValue(docKey, out var builder))
            {
                builder = new DocumentGraphBuilder(_anchors);
                _builders[docKey] = builder;
            }

            switch (node.Type)
            {
                case DocNodeType.Section:
                    builder.ExpandSection(doc, graph, nodeId);
                    break;
                case DocNodeType.TextBlock:
                    builder.ExpandTextBlock(doc, graph, nodeId);
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"节点类型 {node.Type} 不支持展开。只有 Section 和 TextBlock 可以展开。");
            }
        }

        /// <summary>
        /// 刷新缓存的 ContentHash（不重建图）。
        /// 工具修改文档内容后调用此方法，防止下次 GetOrBuildAsync 因 hash 不匹配触发不必要的重建。
        /// CC 锚点会自动跟踪位置，无需重建。
        /// </summary>
        public void RefreshHash(NetOffice.WordApi.Document doc)
        {
            string docKey = doc.FullName;
            if (_cache.TryGetValue(docKey, out var graph))
            {
                graph.ContentHash = doc.Content.Text.GetHashCode();
                Debug.WriteLine($"[GraphCache] 已刷新 hash: {docKey}");
            }
        }

        /// <summary>使指定文档的缓存失效，并清理 CC</summary>
        public void Invalidate(NetOffice.WordApi.Document doc)
        {
            string docKey = doc.FullName;
            CleanupOldAnchors(doc);
            _cache.Remove(docKey);
            _builders.Remove(docKey);
            Debug.WriteLine($"[GraphCache] 已失效: {docKey}");
        }

        /// <summary>使指定文档的缓存失效（按路径）</summary>
        public void Invalidate(string docFullName)
        {
            _cache.Remove(docFullName);
            _builders.Remove(docFullName);
            Debug.WriteLine($"[GraphCache] 已失效: {docFullName}");
        }

        /// <summary>清除所有缓存</summary>
        public void Clear()
        {
            _cache.Clear();
            _builders.Clear();
            Debug.WriteLine("[GraphCache] 全部缓存已清除");
        }

        /// <summary>清理文档中由图构建器创建的 map 锚点（保留用户自定义锚点）</summary>
        private void CleanupOldAnchors(NetOffice.WordApi.Document doc)
        {
            int cleaned = 0;
            // 逆序遍历，只清理 map: 前缀的锚点
            for (int i = doc.ContentControls.Count; i >= 1; i--)
            {
                var cc = doc.ContentControls[i];
                if (cc.Tag != null
                    && cc.Tag.StartsWith(AnchorManager.TagPrefix)
                    && cc.Tag.Substring(AnchorManager.TagPrefix.Length).StartsWith("map:"))
                {
                    cc.Delete(false);
                    cleaned++;
                }
            }
            if (cleaned > 0)
                Debug.WriteLine($"[GraphCache] 清理了 {cleaned} 个 map 锚点");
        }
    }
}
