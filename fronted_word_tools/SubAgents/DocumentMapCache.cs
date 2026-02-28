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
    //  双模式缓存策略：
    //  - 默认返回快速感知 Map（仅大纲级别，无 LLM）
    //  - deep=true 时返回深度感知 Map（含 LLM 推断）
    //  - 深度 Map 优先：如果缓存中已有深度 Map 且未过期，
    //    即使请求快速 Map 也返回深度版本（因为深度版本更完整）
    //  - 通过 doc.Content.Text.GetHashCode() 检测文档内容变更
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
        /// <param name="doc">Word 文档对象</param>
        /// <param name="deep">是否请求深度感知（含 LLM 推断标题层级）</param>
        /// <param name="cancellation">取消令牌</param>
        public async Task<DocumentMap> GetOrBuildAsync(
            NetOffice.WordApi.Document doc,
            bool deep = false,
            CancellationToken cancellation = default)
        {
            string docKey = doc.FullName;
            int contentHash = doc.Content.Text.GetHashCode();

            // 检查缓存
            if (_cache.TryGetValue(docKey, out var cached) && cached.ContentHash == contentHash)
            {
                // 缓存命中：如果请求深度但缓存是快速的，需要重建
                if (deep && !cached.IsDeepPerception)
                {
                    Debug.WriteLine($"[MapCache] 缓存命中但需要升级到深度感知: {docKey}");
                    // 继续往下重建
                }
                else
                {
                    Debug.WriteLine($"[MapCache] 缓存命中: {docKey}, deep={cached.IsDeepPerception}");
                    return cached;
                }
            }

            if (deep)
            {
                Debug.WriteLine($"[MapCache] 构建深度感知 Map: {docKey}");
                return await BuildDeepMapAsync(doc, docKey, contentHash, cancellation);
            }
            else
            {
                Debug.WriteLine($"[MapCache] 构建快速感知 Map: {docKey}");
                return BuildFastMap(doc, docKey, contentHash);
            }
        }

        /// <summary>快速路径：从大纲级别直接构建</summary>
        private DocumentMap BuildFastMap(
            NetOffice.WordApi.Document doc, string docKey, int contentHash)
        {
            var outline = DocumentStructureExtractor.ExtractOutlineOnly(doc);

            var builder = new DocumentAstBuilder();
            var map = builder.BuildFromOutline(outline);
            map.ContentHash = contentHash;

            _cache[docKey] = map;

            Debug.WriteLine($"[MapCache] 快速 Map 已构建并缓存: {map.Index.Count} 个节点");
            return map;
        }

        /// <summary>深度路径：全量提取 + LLM 推断</summary>
        private async Task<DocumentMap> BuildDeepMapAsync(
            NetOffice.WordApi.Document doc, string docKey, int contentHash,
            CancellationToken cancellation)
        {
            var rawStructure = DocumentStructureExtractor.Extract(doc);

            var builder = new DocumentAstBuilder();
            var map = await builder.BuildAsync(rawStructure, cancellation);
            map.ContentHash = contentHash;

            _cache[docKey] = map;

            Debug.WriteLine($"[MapCache] 深度 Map 已构建并缓存: {map.Index.Count} 个节点");
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
