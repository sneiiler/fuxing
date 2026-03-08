using System;
using System.Collections.Generic;

namespace FuXingAgent.Core
{
    public sealed class DocumentFactItem
    {
        public string Type { get; set; }
        public string Summary { get; set; }
        public string Value { get; set; }
        public string Evidence { get; set; }
        public string ContextSnippet { get; set; }
        public string SectionTitle { get; set; }
        public string NodeId { get; set; }
        public int RangeStart { get; set; }
        public int RangeEnd { get; set; }
    }

    public sealed class DocumentFactSnapshot
    {
        public string DocumentPath { get; set; }
        public string DocumentName { get; set; }
        public string Scope { get; set; }
        public int ContentHash { get; set; }
        public int AnalyzedSectionCount { get; set; }
        public DateTime BuiltAt { get; set; }
        public List<DocumentFactItem> Facts { get; set; } = new List<DocumentFactItem>();
    }
}
