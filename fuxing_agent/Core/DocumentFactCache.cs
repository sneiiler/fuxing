using System;
using System.Collections.Generic;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 轻量事实缓存。第一版仅缓存全文事实感知结果。
    /// </summary>
    public sealed class DocumentFactCache
    {
        private static DocumentFactCache _instance;

        public static DocumentFactCache Instance =>
            _instance ?? (_instance = new DocumentFactCache());

        private readonly Dictionary<string, DocumentFactSnapshot> _cache =
            new Dictionary<string, DocumentFactSnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <param name="contentHash">调用方预先计算的内容哈希，避免重复读取 doc.Content.Text</param>
        public DocumentFactSnapshot GetFreshSnapshot(string docFullName, int contentHash, string scope, int minSectionCount)
        {
            if (string.IsNullOrEmpty(docFullName) || !string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!_cache.TryGetValue(docFullName, out var snapshot))
                return null;

            if (snapshot.ContentHash != contentHash)
            {
                _cache.Remove(docFullName);
                return null;
            }

            if (snapshot.AnalyzedSectionCount < minSectionCount)
                return null;

            return snapshot;
        }

        public void Set(string docFullName, DocumentFactSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(docFullName) || snapshot == null) return;
            _cache[docFullName] = snapshot;
        }

        public void Invalidate(string docFullName)
        {
            if (string.IsNullOrEmpty(docFullName)) return;
            _cache.Remove(docFullName);
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
