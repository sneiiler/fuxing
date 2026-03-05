using System;
using System.Collections.Generic;
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
    //  - 缓存失效时自动重建
    //  - 深度图优先：如缓存已有深度版本，即使请求快速也返回深度版本
    // ═══════════════════════════════════════════════════════════════

    /// <summary>文档图缓存变更事件参数</summary>
    public class GraphCacheChangedEventArgs : EventArgs
    {
        /// <summary>变更的文档路径</summary>
        public string DocumentPath { get; }

        /// <summary>当前缓存的文档图（失效时为 null）</summary>
        public DocumentGraph Graph { get; }

        public GraphCacheChangedEventArgs(string documentPath, DocumentGraph graph)
        {
            DocumentPath = documentPath;
            Graph = graph;
        }
    }

    /// <summary>
    /// 文档图缓存。单例模式，跨工具调用共享。
    /// </summary>
    public class DocumentGraphCache
    {
        private static DocumentGraphCache _instance;

        /// <summary>全局单例</summary>
        public static DocumentGraphCache Instance =>
            _instance ?? (_instance = new DocumentGraphCache());

        /// <summary>缓存变更事件（图构建完成 / 缓存失效时触发）</summary>
        public event EventHandler<GraphCacheChangedEventArgs> CacheChanged;

        /// <summary>缓存存储：文档全路径 → DocumentGraph</summary>
        private readonly Dictionary<string, DocumentGraph> _cache
            = new Dictionary<string, DocumentGraph>();

        /// <summary>Builder 缓存：每个文档共享一个 Builder，保持 ID 计数器连续</summary>
        private readonly Dictionary<string, DocumentGraphBuilder> _builders
            = new Dictionary<string, DocumentGraphBuilder>();

        /// <summary>
        /// 获取节点对应的 Word Range。使用节点 Meta 中存储的字符偏移。
        /// </summary>
        public NetOffice.WordApi.Range GetNodeRange(
            NetOffice.WordApi.Document doc, DocNode node)
        {
            if (node.Meta != null
                && node.Meta.TryGetValue("range_start", out var s)
                && node.Meta.TryGetValue("range_end", out var e))
            {
                return doc.Range(int.Parse(s), int.Parse(e));
            }
            throw new System.InvalidOperationException(
                $"无法定位节点 [{node.Id}] {node.Title}：无位置元数据");
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
                    DebugLogger.Instance.LogDebug("GraphCache", $"缓存命中但需升级到深度感知: {docKey}");
                    // 继续重建
                }
                else
                {
                    DebugLogger.Instance.LogDebug("GraphCache", $"缓存命中: {docKey}");
                    return cached;
                }
            }

            // 保存旧图的 label 索引（用于迁移到新图）
            Dictionary<string, string> oldLabels = null;
            if (cached != null && cached.LabelIndex.Count > 0)
                oldLabels = new Dictionary<string, string>(cached.LabelIndex);

            // 缓存失效 → 重建
            var builder = new DocumentGraphBuilder();
            DocumentGraph graph;

            if (deep)
            {
                DebugLogger.Instance.LogDebug("GraphCache", $"构建深度图: {docKey}");
                graph = await builder.BuildFullDeepAsync(doc, cancellation);
            }
            else
            {
                DebugLogger.Instance.LogDebug("GraphCache", $"构建快速图: {docKey}");
                graph = builder.BuildFull(doc);
            }

            // 记录内容 hash
            graph.ContentHash = doc.Content.Text.GetHashCode();

            // 迁移旧 label（只保留新图中仍存在的节点）
            if (oldLabels != null)
            {
                foreach (var kv in oldLabels)
                {
                    var node = graph.GetById(kv.Value);
                    if (node != null)
                    {
                        graph.SetLabel(kv.Value, kv.Key);
                        DebugLogger.Instance.LogDebug("GraphCache", $"迁移 label: {kv.Key} → {kv.Value}");
                    }
                }
            }

            _cache[docKey] = graph;
            _builders[docKey] = builder; // 缓存 builder，后续 expand 复用

            DebugLogger.Instance.LogDebug("GraphCache", $"图已缓存: {graph.Index.Count} 个节点");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docKey, graph));
            return graph;
        }

        /// <summary>展开节点的内部内容（TextBlock→段落）</summary>
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
                builder = new DocumentGraphBuilder();
                _builders[docKey] = builder;
            }

            switch (node.Type)
            {
                case DocNodeType.Section:
                    // Section 已在 map 时完整展开，无需额外操作
                    break;
                case DocNodeType.TextBlock:
                    builder.ExpandTextBlock(doc, graph, nodeId);
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"节点类型 {node.Type} 不支持展开。只有 TextBlock 可以展开到段落级。");
            }
        }

        /// <summary>
        /// 文档内容变更后失效缓存，下次访问时自动重建。
        /// 零标记架构下，文档编辑后 Meta 位置已过期，必须重建。
        /// </summary>
        public void RefreshHash(NetOffice.WordApi.Document doc)
        {
            string docKey = doc.FullName;
            _cache.Remove(docKey);
            DebugLogger.Instance.LogDebug("GraphCache", $"编辑后失效缓存: {docKey}");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docKey, null));
        }

        /// <summary>使指定文档的缓存失效</summary>
        public void Invalidate(NetOffice.WordApi.Document doc)
        {
            string docKey = doc.FullName;
            _cache.Remove(docKey);
            _builders.Remove(docKey);
            DebugLogger.Instance.LogDebug("GraphCache", $"已失效: {docKey}");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docKey, null));
        }

        /// <summary>使指定文档的缓存失效（按路径）</summary>
        public void Invalidate(string docFullName)
        {
            _cache.Remove(docFullName);
            _builders.Remove(docFullName);
            DebugLogger.Instance.LogDebug("GraphCache", $"已失效: {docFullName}");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docFullName, null));
        }

        /// <summary>清除所有缓存</summary>
        public void Clear()
        {
            _cache.Clear();
            _builders.Clear();
            DebugLogger.Instance.LogDebug("GraphCache", "全部缓存已清除");
        }

        /// <summary>查询指定文档的已缓存文档图（不触发构建）</summary>
        public DocumentGraph GetCached(string docFullName)
        {
            _cache.TryGetValue(docFullName, out var graph);
            return graph;
        }

    }
}
