using FuXingAgent.Agents;
using FuXingAgent.Core;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Workflows
{
    public class ExtractDocumentFactsWorkflow
    {
        private readonly Connect _connect;

        private const string WorkflowName = "extract_document_facts_workflow";
        private const string WorkflowDisplayName = "Fact Extraction Workflow";
        private const int TotalSteps = 4;
        private const int MaxSectionsDefault = 12;
        private const int MaxSectionChars = 2600;
        private const int MaxContextChars = 180;

        public ExtractDocumentFactsWorkflow(Connect connect)
        {
            _connect = connect;
        }

        [Description("Extract key facts from the current document for logical review. Focus only on data, events, activities, and technical metrics. Preserve evidence snippets and local context for preview. This workflow does not edit the document.")]
        public string extract_document_facts_workflow(
            [Description("Extraction scope: all=whole document, selection=current selection")] string scope = "all",
            [Description("Maximum number of sections to analyze in this run")] int max_sections = MaxSectionsDefault)
        {
            WorkflowProgressReporter.StartWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps);

            try
            {
                if (max_sections <= 0)
                    max_sections = MaxSectionsDefault;

                // STA: 一次性读取所有文档数据到内存
                string docFullName = null;
                string docName = null;
                string docText = null;
                int contentHash = 0;
                DocumentFactSnapshot cached = null;

                // STA: 一次性读取所有文档数据到内存（只读一次 doc.Content.Text）
                StaHelper.RunOnSta(() =>
                {
                    var app = _connect.WordApplication;
                    var doc = app.ActiveDocument ?? throw new InvalidOperationException("No active document.");
                    docFullName = doc.FullName;
                    docName = doc.Name;

                    // 先读一次全文，用于哈希和后续分析
                    string rawText = doc.Content.Text ?? string.Empty;
                    contentHash = rawText.GetHashCode();

                    cached = DocumentFactCache.Instance.GetFreshSnapshot(docFullName, contentHash, scope, max_sections);
                    if (cached != null) return;

                    if (string.Equals(scope, "selection", StringComparison.OrdinalIgnoreCase))
                    {
                        var selection = app.Selection;
                        if (selection == null || selection.Range == null)
                            throw new InvalidOperationException("Cannot access current selection.");
                        docText = NormalizeText(selection.Range.Text);
                        if (string.IsNullOrWhiteSpace(docText))
                            throw new InvalidOperationException("Selection has no analyzable text.");
                    }
                    else
                    {
                        docText = NormalizeText(rawText);
                    }
                });

                WorkflowProgressReporter.StartStep(WorkflowName, 1, TotalSteps, "Cache Check", "Reuse cached extraction when possible.");
                if (cached != null)
                {
                    WorkflowProgressReporter.FinishStep(WorkflowName, 1, TotalSteps, "Cache Check", true, "Cache hit.");
                    WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "Loaded cached facts.");
                    return BuildPreview(cached, true);
                }
                WorkflowProgressReporter.FinishStep(WorkflowName, 1, TotalSteps, "Cache Check", true, "Cache miss.");

                // 后台线程：从内存文本分块（无 COM 调用）
                WorkflowProgressReporter.StartStep(WorkflowName, 2, TotalSteps, "Chunk Preparation", $"Scope={scope}, maxSections={max_sections}");
                var chunks = CollectChunksFromText(docText, scope, max_sections);
                if (chunks.Count == 0)
                {
                    WorkflowProgressReporter.FinishStep(WorkflowName, 2, TotalSteps, "Chunk Preparation", true, "No analyzable text.");
                    WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "No analyzable content.");
                    return "No analyzable content was found.";
                }
                WorkflowProgressReporter.FinishStep(WorkflowName, 2, TotalSteps, "Chunk Preparation", true, $"Prepared {chunks.Count} chunks.");

                // 后台线程：LLM 提取事实（不阻塞 UI）
                WorkflowProgressReporter.StartStep(WorkflowName, 3, TotalSteps, "Fact Extraction", "Extract data/event/activity/metric from each chunk.");
                var facts = new List<DocumentFactItem>();
                foreach (var chunk in chunks)
                    facts.AddRange(ExtractFactsFromChunk(chunk));
                WorkflowProgressReporter.FinishStep(WorkflowName, 3, TotalSteps, "Fact Extraction", true, $"Collected {facts.Count} raw facts.");

                WorkflowProgressReporter.StartStep(WorkflowName, 4, TotalSteps, "Finalize Preview", "Deduplicate facts and build preview.");
                facts = DeduplicateFacts(facts);

                var snapshot = new DocumentFactSnapshot
                {
                    DocumentPath = docFullName,
                    DocumentName = docName,
                    Scope = scope,
                    ContentHash = contentHash,
                    AnalyzedSectionCount = chunks.Count,
                    BuiltAt = DateTime.Now,
                    Facts = facts
                };

                // 缓存写入不需要 COM 操作
                if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
                    DocumentFactCache.Instance.Set(docFullName, snapshot);

                WorkflowProgressReporter.FinishStep(WorkflowName, 4, TotalSteps, "Finalize Preview", true, $"Kept {facts.Count} facts.");
                WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, true, "Fact extraction completed.");
                return BuildPreview(snapshot, false);
            }
            catch (Exception ex)
            {
                WorkflowProgressReporter.FinishWorkflow(WorkflowName, WorkflowDisplayName, TotalSteps, false, ex.Message);
                throw;
            }
        }

        /// <summary>从预读的内存文本分块（无 COM 调用）</summary>
        private static List<DocumentChunk> CollectChunksFromText(string text, string scope, int maxSections)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<DocumentChunk>();

            if (string.Equals(scope, "selection", StringComparison.OrdinalIgnoreCase))
            {
                return new List<DocumentChunk>
                {
                    new DocumentChunk
                    {
                        SectionTitle = "Selection",
                        NodeId = "selection",
                        Text = Truncate(text, MaxSectionChars)
                    }
                };
            }

            var paragraphs = text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            var chunks = new List<DocumentChunk>();
            var builder = new StringBuilder();
            int chunkIndex = 1;

            foreach (var paragraph in paragraphs)
            {
                if (builder.Length + paragraph.Length + 1 > MaxSectionChars && builder.Length > 0)
                {
                    chunks.Add(new DocumentChunk
                    {
                        SectionTitle = $"Chunk {chunkIndex}",
                        NodeId = $"chunk_{chunkIndex}",
                        Text = builder.ToString().Trim()
                    });
                    builder.Clear();
                    chunkIndex++;
                    if (chunks.Count >= maxSections) break;
                }

                builder.AppendLine(paragraph);
            }

            if (builder.Length > 0 && chunks.Count < maxSections)
            {
                chunks.Add(new DocumentChunk
                {
                    SectionTitle = $"Chunk {chunkIndex}",
                    NodeId = $"chunk_{chunkIndex}",
                    Text = builder.ToString().Trim()
                });
            }

            return chunks;
        }

        private List<DocumentFactItem> ExtractFactsFromChunk(DocumentChunk chunk)
        {
            var client = _connect.AgentBootstrapInstance?.ChatClient
                ?? throw new InvalidOperationException("Agent is not initialized.");

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System,
                    "You extract facts from documents. Return only JSON array with fields: type, summary, value, evidence. " +
                    "Allowed types: data, event, activity, metric. Do not infer unsupported facts."),
                new ChatMessage(ChatRole.User, BuildChunkPrompt(chunk))
            };

            var response = client.GetResponseAsync(messages, new ChatOptions { Temperature = 0.1f })
                .GetAwaiter().GetResult();

            return ParseFacts(response?.Text, chunk);
        }

        private static string BuildChunkPrompt(DocumentChunk chunk)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Extract factual items from the following document chunk.");
            sb.AppendLine("Keep only data, event, activity, and metric facts.");
            sb.AppendLine("Return JSON array only.");
            sb.AppendLine($"Section: {chunk.SectionTitle}");
            sb.AppendLine("Content:");
            sb.AppendLine(chunk.Text);
            return sb.ToString();
        }

        private static List<DocumentFactItem> ParseFacts(string responseText, DocumentChunk chunk)
        {
            string json = ExtractJsonArray(responseText);
            if (string.IsNullOrWhiteSpace(json))
                return new List<DocumentFactItem>();

            try
            {
                var rows = JsonSerializer.Deserialize<List<FactRow>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (rows == null) return new List<DocumentFactItem>();

                var facts = new List<DocumentFactItem>();
                foreach (var row in rows)
                {
                    if (!IsSupportedFactType(row.Type)) continue;
                    if (string.IsNullOrWhiteSpace(row.Summary) || string.IsNullOrWhiteSpace(row.Evidence)) continue;

                    facts.Add(new DocumentFactItem
                    {
                        Type = row.Type.Trim().ToLowerInvariant(),
                        Summary = row.Summary.Trim(),
                        Value = (row.Value ?? string.Empty).Trim(),
                        Evidence = row.Evidence.Trim(),
                        ContextSnippet = BuildContextSnippet(chunk.Text, row.Evidence),
                        SectionTitle = chunk.SectionTitle,
                        NodeId = chunk.NodeId,
                        RangeStart = chunk.RangeStart,
                        RangeEnd = chunk.RangeEnd
                    });
                }
                return facts;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ExtractDocumentFactsWorkflow.ParseFacts", ex);
                return new List<DocumentFactItem>();
            }
        }

        private static bool IsSupportedFactType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            switch (type.Trim().ToLowerInvariant())
            {
                case "data":
                case "event":
                case "activity":
                case "metric":
                    return true;
                default:
                    return false;
            }
        }

        private static string ExtractJsonArray(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string cleaned = Regex.Replace(text, @"<think>[\s\S]*?</think>", string.Empty, RegexOptions.IgnoreCase).Trim();
            int start = cleaned.IndexOf('[');
            int end = cleaned.LastIndexOf(']');
            if (start < 0 || end <= start) return null;
            return cleaned.Substring(start, end - start + 1);
        }

        private static List<DocumentFactItem> DeduplicateFacts(List<DocumentFactItem> facts)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<DocumentFactItem>();

            foreach (var fact in facts)
            {
                string key = string.Join("|", fact.Type, fact.SectionTitle, fact.Summary, fact.Evidence);
                if (!seen.Add(key)) continue;
                result.Add(fact);
            }

            return result;
        }

        private static string BuildPreview(DocumentFactSnapshot snapshot, bool fromCache)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Fact Extraction Preview ===");
            sb.AppendLine($"Document: {snapshot.DocumentName}");
            sb.AppendLine($"Scope: {snapshot.Scope}");
            sb.AppendLine($"Analyzed chunks: {snapshot.AnalyzedSectionCount}");
            sb.AppendLine($"Fact count: {snapshot.Facts.Count}");
            sb.AppendLine($"Generated at: {snapshot.BuiltAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Source: {(fromCache ? "cache" : "live")}");
            sb.AppendLine();

            foreach (string type in new[] { "data", "event", "activity", "metric" })
            {
                var group = snapshot.Facts.Where(f => f.Type == type).ToList();
                if (group.Count == 0) continue;

                sb.AppendLine($"## {type} ({group.Count})");
                int index = 1;
                foreach (var fact in group)
                {
                    sb.AppendLine($"{index}. {fact.Summary}");
                    if (!string.IsNullOrWhiteSpace(fact.Value))
                        sb.AppendLine($"   Value: {fact.Value}");
                    sb.AppendLine($"   Section: {fact.SectionTitle}");
                    sb.AppendLine($"   Evidence: {fact.Evidence}");
                    sb.AppendLine($"   Context: {fact.ContextSnippet}");
                    index++;
                }
                sb.AppendLine();
            }

            if (snapshot.Facts.Count == 0)
                sb.AppendLine("No explicit facts were extracted.");

            return sb.ToString().TrimEnd();
        }

        private static string BuildContextSnippet(string text, string evidence)
        {
            string normalizedText = NormalizeText(text);
            string normalizedEvidence = NormalizeText(evidence);

            if (string.IsNullOrWhiteSpace(normalizedEvidence))
                return Truncate(normalizedText, MaxContextChars);

            int idx = normalizedText.IndexOf(normalizedEvidence, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return Truncate(normalizedEvidence, MaxContextChars);

            int start = Math.Max(0, idx - 50);
            int end = Math.Min(normalizedText.Length, idx + normalizedEvidence.Length + 50);
            string snippet = normalizedText.Substring(start, end - start).Trim();

            if (start > 0) snippet = "..." + snippet;
            if (end < normalizedText.Length) snippet += "...";
            return Truncate(snippet, MaxContextChars);
        }

        private static string SafeReadRangeText(Word.Range range)
        {
            try { return range?.Text ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string normalized = text.Replace("\r", "\n").Replace("\a", " ").Replace("\v", " ");
            normalized = Regex.Replace(normalized, @"\n{2,}", "\n");
            normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
            return normalized.Trim();
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "...";
        }

        private sealed class DocumentChunk
        {
            public string SectionTitle { get; set; }
            public string NodeId { get; set; }
            public int RangeStart { get; set; }
            public int RangeEnd { get; set; }
            public string Text { get; set; }
        }

        private sealed class FactRow
        {
            public string Type { get; set; }
            public string Summary { get; set; }
            public string Value { get; set; }
            public string Evidence { get; set; }
        }
    }
}
