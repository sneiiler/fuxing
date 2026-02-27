using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  文档 Map 缓存
    //
    //  通过 doc.Content.Text.GetHashCode() 检测文档内容变更，
    //  仅在内容实际改变时才重新构建 AST。
    //  缓存粒度：按文档全路径（FullName）隔离。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档 Map 缓存。单例模式，跨工具调用共享。
    /// </summary>
    public class DocumentMapCache
    {
        private static DocumentMapCache _instance;

        /// <summary>全局单例</summary>
        public static DocumentMapCache Instance =>
            _instance ?? (_instance = new DocumentMapCache());

        /// <summary>缓存存储：文档全路径 → DocumentMap</summary>
        private readonly Dictionary<string, DocumentMap> _cache
            = new Dictionary<string, DocumentMap>();

        /// <summary>
        /// 获取文档的 Map。优先从缓存返回；文档内容变更时自动重建。
        /// </summary>
        public async Task<DocumentMap> GetOrBuildAsync(
            NetOffice.WordApi.Document doc,
            CancellationToken cancellation = default)
        {
            string docKey = doc.FullName;
            int contentHash = doc.Content.Text.GetHashCode();

            if (_cache.TryGetValue(docKey, out var cached) && cached.ContentHash == contentHash)
            {
                Debug.WriteLine($"[MapCache] 缓存命中: {docKey}, hash={contentHash}");
                return cached;
            }

            Debug.WriteLine($"[MapCache] 缓存未命中，重建 AST: {docKey}");

            // 提取段落元数据（Word COM，在 STA/UI 线程）
            var rawStructure = DocumentStructureExtractor.Extract(doc);

            // 构建 AST（可能触发 LLM 调用）
            var builder = new DocumentAstBuilder();
            var map = await builder.BuildAsync(rawStructure, cancellation);
            map.ContentHash = contentHash;

            // 写入缓存
            _cache[docKey] = map;

            Debug.WriteLine($"[MapCache] AST 已构建并缓存: {map.Index.Count} 个节点, " +
                            $"LLM辅助={map.LlmAssisted}");
            return map;
        }

        /// <summary>使指定文档的缓存失效</summary>
        public void Invalidate(string docFullName)
        {
            if (_cache.Remove(docFullName))
                Debug.WriteLine($"[MapCache] 已失效: {docFullName}");
        }

        /// <summary>清除所有缓存</summary>
        public void Clear()
        {
            _cache.Clear();
            Debug.WriteLine("[MapCache] 全部缓存已清除");
        }
    }
}
