using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Core
{
    /// <summary>文档图缓存变更事件参数</summary>
    public class GraphCacheChangedEventArgs : EventArgs
    {
        public string DocumentPath { get; }
        public DocumentGraph Graph { get; }

        public GraphCacheChangedEventArgs(string documentPath, DocumentGraph graph)
        {
            DocumentPath = documentPath;
            Graph = graph;
        }
    }

    /// <summary>
    /// 文档图缓存。单例模式，跨工具调用共享。
    /// 深度图优先：如缓存已有深度版本，即使请求快速也返回深度版本。
    /// </summary>
    public class DocumentGraphCache
    {
        private static DocumentGraphCache _instance;

        public static DocumentGraphCache Instance =>
            _instance ?? (_instance = new DocumentGraphCache());

        public event EventHandler<GraphCacheChangedEventArgs> CacheChanged;

        private readonly Dictionary<string, DocumentGraph> _cache
            = new Dictionary<string, DocumentGraph>();

        private readonly Dictionary<string, DocumentGraphBuilder> _builders
            = new Dictionary<string, DocumentGraphBuilder>();

        /// <summary>获取节点对应的 Word Range</summary>
        public Word.Range GetNodeRange(Word.Document doc, DocNode node)
        {
            if (node.Meta != null
                && node.Meta.TryGetValue("range_start", out var s)
                && node.Meta.TryGetValue("range_end", out var e))
            {
                return doc.Range(int.Parse(s), int.Parse(e));
            }
            throw new InvalidOperationException(
                $"无法定位节点 [{node.Id}] {node.Title}：无位置元数据");
        }

        /// <summary>获取文档图。优先从缓存返回；文档内容变更时自动重建。</summary>
        public async Task<DocumentGraph> GetOrBuildAsync(
            Word.Document doc,
            Agents.SubAgentRunner subAgentRunner,
            bool deep = false,
            CancellationToken cancellation = default,
            IProgress<(float ratio, string message)> progress = null)
        {
            string docKey = doc.FullName;
            int contentHash = doc.Content.Text.GetHashCode();

            if (_cache.TryGetValue(docKey, out var cached) && cached.ContentHash == contentHash)
            {
                if (deep && !cached.IsDeepPerception)
                {
                    DebugLogger.Instance.LogDebug("GraphCache", $"缓存命中但需升级到深度感知: {docKey}");
                }
                else
                {
                    DebugLogger.Instance.LogDebug("GraphCache", $"缓存命中: {docKey}");
                    return cached;
                }
            }

            Dictionary<string, string> oldLabels = null;
            if (cached != null && cached.LabelIndex.Count > 0)
                oldLabels = new Dictionary<string, string>(cached.LabelIndex);

            // 让出 UI 线程，使调用方的 UI 更新（如"正在感知…"标签）有机会渲染
            await Task.Yield();

            var builder = new DocumentGraphBuilder();
            DocumentGraph graph;

            if (deep)
            {
                DebugLogger.Instance.LogDebug("GraphCache", $"构建深度图: {docKey}");
                graph = await builder.BuildFullDeepAsync(doc, subAgentRunner, cancellation);
            }
            else
            {
                DebugLogger.Instance.LogDebug("GraphCache", $"构建快速图: {docKey}");
                graph = await RunOnStaThreadAsync(() => builder.BuildFull(doc, progress), cancellation);
            }

            graph.ContentHash = contentHash;

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
            _builders[docKey] = builder;

            DebugLogger.Instance.LogDebug("GraphCache", $"图已缓存: {graph.Index.Count} 个节点");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docKey, graph));
            return graph;
        }

        /// <summary>展开节点的内部内容（TextBlock → 段落）</summary>
        public void ExpandNode(Word.Document doc, string nodeId)
        {
            string docKey = doc.FullName;
            if (!_cache.TryGetValue(docKey, out var graph))
                throw new InvalidOperationException("请先构建文档图");

            var node = graph.GetById(nodeId);
            if (node == null)
                throw new InvalidOperationException($"节点不存在: {nodeId}");

            if (!_builders.TryGetValue(docKey, out var builder))
            {
                builder = new DocumentGraphBuilder();
                _builders[docKey] = builder;
            }

            switch (node.Type)
            {
                case DocNodeType.Section:
                    break;
                case DocNodeType.TextBlock:
                    builder.ExpandTextBlock(doc, graph, nodeId);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"节点类型 {node.Type} 不支持展开。只有 TextBlock 可以展开到段落级。");
            }
        }

        /// <summary>文档内容变更后失效缓存</summary>
        public void RefreshHash(Word.Document doc)
        {
            string docKey = doc.FullName;
            _cache.Remove(docKey);
            DebugLogger.Instance.LogDebug("GraphCache", $"编辑后失效缓存: {docKey}");
            CacheChanged?.Invoke(this, new GraphCacheChangedEventArgs(docKey, null));
        }

        /// <summary>使指定文档的缓存失效</summary>
        public void Invalidate(Word.Document doc)
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

        private static Task<T> RunOnStaThreadAsync<T>(Func<T> work, CancellationToken cancellation)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellation.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    var result = work();
                    tcs.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (cancellation.CanBeCanceled)
            {
                cancellation.Register(() => tcs.TrySetCanceled());
            }

            return tcs.Task;
        }
    }
}
