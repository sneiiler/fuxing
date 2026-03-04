using FuXing.SubAgents;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  文档感知：图构建器
    //
    //  统一入口，负责从 Word 文档构建 DocumentGraph。
    //
    //  双路径：
    //  - 快速路径 BuildFull()：从大纲级别程序化构建（无 LLM）
    //  - 深度路径 BuildFullDeepAsync()：全量段落提取 + LLM 推断标题
    //
    //  内部包含：
    //  - 段落结构提取（深度模式 COM 属性采集）
    //  - AST 构建（LLM 标题层级推断）
    //  - 图节点创建与层级组织
    //  - 静态文档工具方法（标题查找、章节范围、只读打开）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>表示文档中一个标题段落及其大纲级别</summary>
    public class HeadingInfo
    {
        public Paragraph Paragraph { get; set; }
        public int Level { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// 文档图构建器。扫描文档结构，创建位置标定的节点图。
    /// </summary>
    public class DocumentGraphBuilder
    {
        private int _sectionCounter;
        private int _headingCounter;
        private int _preambleCounter;
        private int _tableCounter;
        private int _imageCounter;
        private int _textBlockCounter;
        private int _listCounter;
        private int _paragraphCounter;

        // ─── AST 构建常量 ───
        private const int MaxSampleParagraphs = 50;
        private const int SingleLineCandidateMaxLength = 80;

        // ─── 结构提取常量 ───
        private const int MaxTextLength = 120;

        public DocumentGraphBuilder()
        {
        }

        // ═══════════════════════════════════════════════════
        //  公共 API：图构建
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 快速路径：从大纲级别构建完整文档图（Section + Heading + 内容元素一步到位）。
        /// </summary>
        public DocumentGraph BuildFull(Document doc)
        {
            var sw = Stopwatch.StartNew();
            ResetCounters();

            var graph = new DocumentGraph
            {
                DocumentName = doc.Name,
                IsDeepPerception = false,
                BuiltAt = DateTime.Now
            };

            var root = CreateRootNode(doc);
            graph.Root = root;
            graph.AddNode(root);

            var headings = ExtractHeadings(doc);

            BuildPreamble(doc, graph, root, headings);

            if (headings.Count > 0)
            {
                BuildSectionHierarchy(doc, graph, root, headings);
                BuildSiblingLinks(graph, root);
                DiscoverAllSectionContents(doc, graph);
            }

            sw.Stop();
            DebugLogger.Instance.LogDebug("GraphBuilder",
                $"文档图构建完成: {graph.Index.Count} 个节点, 耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        /// <summary>
        /// 深度路径：全量段落提取 + LLM 推断标题，适用于不规范文档。
        /// </summary>
        public async Task<DocumentGraph> BuildFullDeepAsync(
            Document doc, CancellationToken cancellation = default)
        {
            var sw = Stopwatch.StartNew();
            ResetCounters();

            var rawStructure = ExtractDeepStructure(doc);
            var astRoot = await BuildAstAsync(rawStructure, cancellation);

            var graph = new DocumentGraph
            {
                DocumentName = doc.Name,
                IsDeepPerception = true,
                BuiltAt = DateTime.Now
            };

            var root = CreateRootNode(doc);
            graph.Root = root;
            graph.AddNode(root);

            var headings = CollectHeadingsFromAst(astRoot);

            BuildPreamble(doc, graph, root, headings);

            if (headings.Count > 0)
            {
                BuildSectionHierarchy(doc, graph, root, headings);
                BuildSiblingLinks(graph, root);
                DiscoverAllSectionContents(doc, graph);
            }

            sw.Stop();
            DebugLogger.Instance.LogDebug("GraphBuilder",
                $"深度文档图构建完成: {graph.Index.Count} 个节点, 耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        /// <summary>
        /// 展开一个 TextBlock 节点，为其中每个段落创建 Paragraph 子节点。
        /// </summary>
        public void ExpandTextBlock(Document doc, DocumentGraph graph, string textBlockNodeId)
        {
            var block = graph.GetById(textBlockNodeId);
            if (block == null)
                throw new InvalidOperationException($"节点不存在: {textBlockNodeId}");
            if (block.Type != DocNodeType.TextBlock)
                throw new InvalidOperationException($"只能展开 TextBlock 节点，当前类型: {block.Type}");
            if (block.Expanded)
                return;

            var blockRange = GetNodeRange(doc, block);
            int blockStart = blockRange.Start;
            int blockEnd = blockRange.End;

            var paraNodes = new List<DocNode>();
            int paraIdx = 0;

            foreach (Paragraph para in doc.Paragraphs)
            {
                var paraRange = para.Range;
                int ps = paraRange.Start;
                int pe = paraRange.End;

                if (ps < blockStart || ps >= blockEnd) continue;

                string text = paraRange.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                paraIdx++;
                string nodeId = NextId("p");

                var node = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Paragraph,
                    Title = $"段落{paraIdx}",
                    Preview = text.Length > 100 ? text.Substring(0, 100) + "…" : text,
                    ParentId = block.Id,
                    Meta = new Dictionary<string, string>
                    {
                        ["range_start"] = ps.ToString(),
                        ["range_end"] = pe.ToString()
                    }
                };

                graph.AddNode(node);
                paraNodes.Add(node);
            }

            block.ChildIds = paraNodes.Select(n => n.Id).ToList();
            RebuildSiblingLinks(graph, block);
            block.Expanded = true;

            DebugLogger.Instance.LogDebug("GraphBuilder",
                $"展开 TextBlock {textBlockNodeId}: 发现 {paraNodes.Count} 个段落");
        }

        // ═══════════════════════════════════════════════════
        //  静态工具方法
        // ═══════════════════════════════════════════════════

        /// <summary>在文档中按名称查找标题段落（不区分大小写），找不到时返回 null。</summary>
        public static HeadingInfo FindHeading(Document doc, string headingName)
        {
            foreach (Paragraph para in doc.Paragraphs)
            {
                int level = (int)para.OutlineLevel;
                if (level < 1 || level > 6) continue;

                string text = para.Range.Text.Trim();
                if (text.Equals(headingName, StringComparison.OrdinalIgnoreCase))
                {
                    return new HeadingInfo
                    {
                        Paragraph = para,
                        Level = level,
                        Text = text,
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// 计算某标题的章节结束位置（即下一个同级或更高级标题的起始位置）。
        /// 如果找不到后续同级标题，返回文档内容末尾位置。
        /// </summary>
        public static int FindSectionEnd(Document doc, Paragraph targetPara, int targetLevel)
        {
            bool passedTarget = false;
            foreach (Paragraph para in doc.Paragraphs)
            {
                if (para.Range.Start == targetPara.Range.Start)
                {
                    passedTarget = true;
                    continue;
                }

                if (!passedTarget) continue;

                int level = (int)para.OutlineLevel;
                if (level >= 1 && level <= targetLevel)
                    return para.Range.Start;
            }

            return doc.Content.End - 1;
        }

        /// <summary>
        /// 获取指定标题的章节文本范围 [start, end)。
        /// includeHeading=true 时 start 为标题起始位置，否则为标题结束位置。
        /// </summary>
        public static (int Start, int End) GetSectionRange(
            Document doc, Paragraph targetPara, int targetLevel, bool includeHeading)
        {
            int start = includeHeading ? targetPara.Range.Start : targetPara.Range.End;
            int end = FindSectionEnd(doc, targetPara, targetLevel);
            return (start, end);
        }

        /// <summary>
        /// 获取已打开的文档或以只读方式临时打开。
        /// 返回 (doc, shouldClose)：shouldClose=true 表示文档是本次临时打开的，调用方用完后应关闭。
        /// </summary>
        public static (Document Doc, bool ShouldClose) GetOrOpenReadOnly(
            Application app, string filePath)
        {
            string targetPath = System.IO.Path.GetFullPath(filePath).TrimEnd('\\');

            foreach (Document doc in app.Documents)
            {
                string openPath;
                try { openPath = System.IO.Path.GetFullPath(doc.FullName).TrimEnd('\\'); }
                catch { continue; }

                if (string.Equals(openPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    return (doc, false);
            }

            var m = Type.Missing;
            var opened = app.Documents.Open(filePath, false, true, false, m, m, m, m, m, m, m, false);
            return (opened, true);
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：计数器与 ID 生成
        // ═══════════════════════════════════════════════════

        private static Range GetNodeRange(Document doc, DocNode node)
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

        private void ResetCounters()
        {
            _sectionCounter = 0;
            _headingCounter = 0;
            _preambleCounter = 0;
            _tableCounter = 0;
            _imageCounter = 0;
            _textBlockCounter = 0;
            _listCounter = 0;
            _paragraphCounter = 0;
        }

        private string NextId(string prefix)
        {
            switch (prefix)
            {
                case "s": return $"s{++_sectionCounter:D2}";
                case "h": return $"h{++_headingCounter:D2}";
                case "pre": return $"pre{++_preambleCounter:D2}";
                case "t": return $"t{++_tableCounter:D2}";
                case "i": return $"i{++_imageCounter:D2}";
                case "b": return $"b{++_textBlockCounter:D2}";
                case "l": return $"l{++_listCounter:D2}";
                case "p": return $"p{++_paragraphCounter:D2}";
                default: throw new ArgumentException($"未知前缀: {prefix}");
            }
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：快速路径标题提取
        // ═══════════════════════════════════════════════════

        /// <summary>扫描文档段落，提取具有大纲级别的标题</summary>
        private static List<HeadingEntry> ExtractHeadings(Document doc)
        {
            var headings = new List<HeadingEntry>();
            int paraIndex = 0;

            foreach (Paragraph para in doc.Paragraphs)
            {
                paraIndex++;
                int outlineLevel;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { continue; }

                if (outlineLevel < 1 || outlineLevel > 9) continue;

                var range = para.Range;
                string text = range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                headings.Add(new HeadingEntry
                {
                    ParaIndex = paraIndex,
                    Level = outlineLevel,
                    Title = text.Length > 200 ? text.Substring(0, 200) + "…" : text,
                    RangeStart = range.Start,
                    RangeEnd = range.End
                });
            }

            return headings;
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：图层级构建
        // ═══════════════════════════════════════════════════

        /// <summary>用栈算法构建 Section 层级关系</summary>
        private void BuildSectionHierarchy(
            Document doc, DocumentGraph graph, DocNode root, List<HeadingEntry> headings)
        {
            var stack = new Stack<DocNode>();
            stack.Push(root);

            for (int i = 0; i < headings.Count; i++)
            {
                var h = headings[i];

                var headingPara = doc.Paragraphs[h.ParaIndex];
                int headingStart = headingPara.Range.Start;
                int bodyStart = headingPara.Range.End;
                int rangeEnd;
                if (i + 1 < headings.Count)
                    rangeEnd = doc.Paragraphs[headings[i + 1].ParaIndex].Range.Start;
                else
                    rangeEnd = doc.Content.End - 1;

                string nodeId = NextId("s");

                var sectionNode = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Section,
                    Title = h.Title,
                    Level = h.Level,
                    Meta = new Dictionary<string, string>
                    {
                        ["heading_start"] = headingStart.ToString(),
                        ["range_start"] = bodyStart.ToString(),
                        ["range_end"] = rangeEnd.ToString()
                    }
                };

                sectionNode.Preview = ExtractBodyPreview(doc.Range(bodyStart, rangeEnd));

                while (stack.Count > 1 && stack.Peek().Level >= h.Level)
                    stack.Pop();

                var parent = stack.Peek();
                sectionNode.ParentId = parent.Id;
                parent.ChildIds.Add(nodeId);

                graph.AddNode(sectionNode);
                stack.Push(sectionNode);
            }
        }

        /// <summary>递归建立兄弟链（prev/next）</summary>
        private static void BuildSiblingLinks(DocumentGraph graph, DocNode parent)
        {
            for (int i = 0; i < parent.ChildIds.Count; i++)
            {
                var child = graph.GetById(parent.ChildIds[i]);
                if (child == null) continue;

                child.PrevId = i > 0 ? parent.ChildIds[i - 1] : null;
                child.NextId = i < parent.ChildIds.Count - 1 ? parent.ChildIds[i + 1] : null;

                BuildSiblingLinks(graph, child);
            }
        }

        /// <summary>重建某节点下子节点的兄弟链</summary>
        private static void RebuildSiblingLinks(DocumentGraph graph, DocNode parent)
        {
            for (int i = 0; i < parent.ChildIds.Count; i++)
            {
                var child = graph.GetById(parent.ChildIds[i]);
                if (child == null) continue;
                child.PrevId = i > 0 ? parent.ChildIds[i - 1] : null;
                child.NextId = i < parent.ChildIds.Count - 1 ? parent.ChildIds[i + 1] : null;
            }
        }

        /// <summary>提取正文区域预览（第一行非空文本）</summary>
        private static string ExtractBodyPreview(Range bodyRange)
        {
            string text = bodyRange.Text ?? "";
            text = text.TrimStart('\r', '\n', '\a');
            if (string.IsNullOrWhiteSpace(text)) return null;

            int lineEnd = text.IndexOf('\r');
            if (lineEnd >= 0) text = text.Substring(0, lineEnd);
            if (text.Length > 100) text = text.Substring(0, 100) + "…";

            return text.Trim();
        }

        /// <summary>创建根节点</summary>
        private static DocNode CreateRootNode(Document doc)
        {
            return new DocNode
            {
                Id = "doc",
                Type = DocNodeType.Document,
                Title = doc.Name,
                Level = 0
            };
        }

        /// <summary>
        /// 为文档第一个标题之前的内容创建 Preamble 节点。
        /// </summary>
        private void BuildPreamble(Document doc, DocumentGraph graph, DocNode root, List<HeadingEntry> headings)
        {
            int docStart = doc.Content.Start;
            int firstHeadingStart;

            if (headings.Count > 0)
                firstHeadingStart = doc.Paragraphs[headings[0].ParaIndex].Range.Start;
            else
                firstHeadingStart = doc.Content.End - 1;

            if (firstHeadingStart <= docStart) return;

            var preambleRange = doc.Range(docStart, firstHeadingStart);
            string text = preambleRange.Text ?? "";
            if (string.IsNullOrWhiteSpace(text.TrimEnd('\r', '\n', '\a'))) return;

            string nodeId = NextId("pre");

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.Preamble,
                Title = "前言",
                Preview = ExtractBodyPreview(preambleRange),
                ParentId = root.Id,
                Level = 0,
                Meta = new Dictionary<string, string>
                {
                    ["range_start"] = docStart.ToString(),
                    ["range_end"] = firstHeadingStart.ToString()
                }
            };

            graph.AddNode(node);
            root.ChildIds.Insert(0, nodeId);
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：内容元素发现
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 遍历所有 Section 节点，为每个 Section 创建 Heading 子节点并发现内容元素。
        /// </summary>
        private void DiscoverAllSectionContents(Document doc, DocumentGraph graph)
        {
            var sections = graph.FindByType(DocNodeType.Section);

            foreach (var section in sections)
                CreateHeadingNode(doc, graph, section);

            foreach (var section in sections)
            {
                var sectionRange = GetNodeRange(doc, section);
                int sectionStart = sectionRange.Start;
                int sectionEnd = sectionRange.End;

                var existingChildIds = new HashSet<string>(section.ChildIds);
                var contentNodes = new List<DocNode>();
                DiscoverContentElements(doc, graph, sectionStart, sectionEnd,
                    section, existingChildIds, contentNodes);

                MergeContentNodesIntoSection(doc, graph, section, contentNodes);

                section.Expanded = true;
            }

            BuildSiblingLinks(graph, graph.Root);
        }

        /// <summary>为 Section 节点创建对应的 Heading 子节点</summary>
        private void CreateHeadingNode(Document doc, DocumentGraph graph, DocNode section)
        {
            if (section.Meta == null || !section.Meta.TryGetValue("heading_start", out var hsStr))
                return;

            int headingStart = int.Parse(hsStr);
            int headingEnd;
            if (section.Meta.TryGetValue("range_start", out var rsStr))
                headingEnd = int.Parse(rsStr);
            else
                return;

            if (headingEnd <= headingStart) return;

            string nodeId = NextId("h");

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.Heading,
                Title = section.Title,
                ParentId = section.Id,
                Level = section.Level,
                Meta = new Dictionary<string, string>
                {
                    ["range_start"] = headingStart.ToString(),
                    ["range_end"] = headingEnd.ToString()
                }
            };

            graph.AddNode(node);
            section.ChildIds.Insert(0, nodeId);
        }

        /// <summary>扫描 section 范围内的段落，识别 Table/Image/TextBlock/List</summary>
        private void DiscoverContentElements(
            Document doc, DocumentGraph graph,
            int sectionStart, int sectionEnd,
            DocNode parentSection,
            HashSet<string> existingChildIds,
            List<DocNode> outContentNodes)
        {
            var childSectionRanges = new List<(int start, int end)>();
            foreach (var childId in parentSection.ChildIds)
            {
                var child = graph.GetById(childId);
                if (child?.Type != DocNodeType.Section) continue;

                int csStart = child.Meta != null && child.Meta.TryGetValue("heading_start", out var hs)
                    ? int.Parse(hs) : int.MaxValue;
                int csEnd = child.Meta != null && child.Meta.TryGetValue("range_end", out var re)
                    ? int.Parse(re) : int.MinValue;

                if (csStart < csEnd)
                    childSectionRanges.Add((csStart, csEnd));
            }

            var textBlockParas = new List<Paragraph>();
            var listParas = new List<Paragraph>();
            bool inList = false;

            foreach (Paragraph para in doc.Paragraphs)
            {
                var paraRange = para.Range;
                int ps = paraRange.Start;

                if (ps < sectionStart || ps >= sectionEnd) continue;

                bool inChildSection = false;
                foreach (var (cs, ce) in childSectionRanges)
                {
                    if (ps >= cs && ps < ce) { inChildSection = true; break; }
                }
                if (inChildSection)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                    continue;
                }

                int outlineLevel;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { outlineLevel = 10; }
                if (outlineLevel >= 1 && outlineLevel <= 9)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                    continue;
                }

                bool isInTable = false;
                try
                {
                    var info = paraRange.get_Information(WdInformation.wdWithInTable);
                    isInTable = info is bool b && b;
                }
                catch { }

                if (isInTable)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                    continue;
                }

                bool hasImage = false;
                try { hasImage = paraRange.InlineShapes.Count > 0; }
                catch { }

                if (hasImage)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;

                    var imgNode = CreateImageNode(doc, graph, parentSection, paraRange);
                    if (imgNode != null) outContentNodes.Add(imgNode);
                    continue;
                }

                bool isListItem = false;
                try
                {
                    isListItem = para.Range.ListFormat.ListType !=
                                 WdListType.wdListNoNumbering;
                }
                catch { }

                if (isListItem)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    listParas.Add(para);
                    inList = true;
                    continue;
                }

                if (inList)
                {
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                }

                string paraText = paraRange.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                if (!string.IsNullOrWhiteSpace(paraText))
                    textBlockParas.Add(para);
            }

            FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
            FlushList(doc, graph, parentSection, listParas, outContentNodes);

            DiscoverTables(doc, graph, parentSection, sectionStart, sectionEnd,
                childSectionRanges, outContentNodes);
        }

        /// <summary>将累积的文本段落创建为 TextBlock 节点</summary>
        private void FlushTextBlock(Document doc, DocumentGraph graph,
            DocNode parent, List<Paragraph> paras, List<DocNode> outNodes)
        {
            if (paras.Count == 0) return;

            int start = paras[0].Range.Start;
            int end = paras[paras.Count - 1].Range.End;
            string firstText = paras[0].Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";

            string nodeId = NextId("b");

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.TextBlock,
                Title = $"文本块 ({paras.Count}段)",
                Preview = firstText.Length > 100 ? firstText.Substring(0, 100) + "…" : firstText,
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["para_count"] = paras.Count.ToString(),
                    ["range_start"] = start.ToString(),
                    ["range_end"] = end.ToString()
                }
            };

            graph.AddNode(node);
            outNodes.Add(node);

            paras.Clear();
        }

        /// <summary>将累积的列表段落创建为 List 节点</summary>
        private void FlushList(Document doc, DocumentGraph graph,
            DocNode parent, List<Paragraph> paras, List<DocNode> outNodes)
        {
            if (paras.Count == 0) return;

            int start = paras[0].Range.Start;
            int end = paras[paras.Count - 1].Range.End;
            string firstText = paras[0].Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";

            string nodeId = NextId("l");

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.List,
                Title = $"列表 ({paras.Count}项)",
                Preview = firstText.Length > 100 ? firstText.Substring(0, 100) + "…" : firstText,
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["item_count"] = paras.Count.ToString(),
                    ["range_start"] = start.ToString(),
                    ["range_end"] = end.ToString()
                }
            };

            graph.AddNode(node);
            outNodes.Add(node);

            paras.Clear();
        }

        /// <summary>创建 Image 节点</summary>
        private DocNode CreateImageNode(Document doc, DocumentGraph graph,
            DocNode parent, Range paraRange)
        {
            InlineShape shape;
            try { shape = paraRange.InlineShapes[1]; }
            catch { return null; }

            string nodeId = NextId("i");

            float widthCm = 0, heightCm = 0;
            try
            {
                widthCm = (float)Math.Round(shape.Width / 28.35, 1);
                heightCm = (float)Math.Round(shape.Height / 28.35, 1);
            }
            catch { }

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.Image,
                Title = $"图{_imageCounter} ({widthCm}×{heightCm}cm)",
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["width_cm"] = widthCm.ToString("F1"),
                    ["height_cm"] = heightCm.ToString("F1"),
                    ["range_start"] = paraRange.Start.ToString(),
                    ["range_end"] = paraRange.End.ToString()
                }
            };

            graph.AddNode(node);

            return node;
        }

        /// <summary>发现 section 范围内的表格</summary>
        private void DiscoverTables(Document doc, DocumentGraph graph,
            DocNode parent, int sectionStart, int sectionEnd,
            List<(int start, int end)> childSectionRanges,
            List<DocNode> outNodes)
        {
            foreach (Table table in doc.Tables)
            {
                int tableStart = table.Range.Start;
                if (tableStart < sectionStart || tableStart >= sectionEnd)
                    continue;

                bool inChild = false;
                foreach (var (cs, ce) in childSectionRanges)
                {
                    if (tableStart >= cs && tableStart < ce)
                    { inChild = true; break; }
                }
                if (inChild) continue;

                int rows = 0, cols = 0;
                try { rows = table.Rows.Count; cols = table.Columns.Count; }
                catch { }

                string nodeId = NextId("t");

                var node = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Table,
                    Title = $"表{_tableCounter} ({rows}×{cols})",
                    ParentId = parent.Id,
                    Meta = new Dictionary<string, string>
                    {
                        ["rows"] = rows.ToString(),
                        ["cols"] = cols.ToString(),
                        ["range_start"] = tableStart.ToString(),
                        ["range_end"] = table.Range.End.ToString()
                    }
                };

                try
                {
                    string cellText = table.Cell(1, 1).Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                    if (cellText.Length > 60) cellText = cellText.Substring(0, 60) + "…";
                    node.Preview = cellText;
                }
                catch { }

                graph.AddNode(node);
                outNodes.Add(node);
            }
        }

        /// <summary>将新发现的内容节点按文档顺序合并到 section 的子节点列表中</summary>
        private static void MergeContentNodesIntoSection(
            Document doc, DocumentGraph graph, DocNode section, List<DocNode> contentNodes)
        {
            if (contentNodes.Count == 0) return;

            var allChildren = new List<(string id, int position)>();

            foreach (var childId in section.ChildIds)
            {
                var child = graph.GetById(childId);
                if (child == null) continue;

                int pos = child.Meta != null && child.Meta.TryGetValue("range_start", out var rs)
                    ? int.Parse(rs) : int.MaxValue;
                if (child.Type == DocNodeType.Section && child.Meta != null
                    && child.Meta.TryGetValue("heading_start", out var hs))
                    pos = int.Parse(hs);
                allChildren.Add((childId, pos));
            }

            foreach (var node in contentNodes)
            {
                int pos = node.Meta != null && node.Meta.TryGetValue("range_start", out var rs)
                    ? int.Parse(rs) : int.MaxValue;
                allChildren.Add((node.Id, pos));
            }

            allChildren.Sort((a, b) => a.position.CompareTo(b.position));

            section.ChildIds = allChildren.Select(c => c.id).ToList();
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：深度路径 — 段落结构提取
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 深度提取：遍历所有段落，采集字号、加粗、对齐、样式等格式元数据。
        /// 用于文档未使用标准标题样式时，提供给 LLM 推断标题层级。
        /// </summary>
        private static DeepStructure ExtractDeepStructure(Document doc)
        {
            var sw = Stopwatch.StartNew();

            var result = new DeepStructure
            {
                DocumentName = doc.Name,
                TotalParagraphs = doc.Paragraphs.Count
            };

            var fontSizes = new HashSet<float>();
            var styles = new HashSet<string>();
            int index = 0;

            foreach (Paragraph para in doc.Paragraphs)
            {
                index++;
                var range = para.Range;
                var font = range.Font;

                string text = range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                bool isBlank = string.IsNullOrWhiteSpace(text);

                float fontSize = -1;
                try
                {
                    float rawSize = font.Size;
                    fontSize = (rawSize > 0 && rawSize < 1000) ? rawSize : -1;
                }
                catch { }

                bool? bold = null;
                try
                {
                    int rawBold = font.Bold;
                    if (rawBold == -1) bold = true;
                    else if (rawBold == 0) bold = false;
                }
                catch { }

                bool? italic = null;
                try
                {
                    int rawItalic = font.Italic;
                    if (rawItalic == -1) italic = true;
                    else if (rawItalic == 0) italic = false;
                }
                catch { }

                string alignment = "左对齐";
                try
                {
                    switch (para.Alignment)
                    {
                        case WdParagraphAlignment.wdAlignParagraphCenter:
                            alignment = "居中"; break;
                        case WdParagraphAlignment.wdAlignParagraphRight:
                            alignment = "右对齐"; break;
                        case WdParagraphAlignment.wdAlignParagraphJustify:
                            alignment = "两端对齐"; break;
                        default:
                            alignment = "左对齐"; break;
                    }
                }
                catch { }

                string styleName = "";
                try { styleName = ((dynamic)para.Style).NameLocal?.ToString() ?? ""; }
                catch { }

                int outlineLevel = 10;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { }

                bool isListItem = false;
                int listLevel = -1;
                try
                {
                    var listFormat = para.Range.ListFormat;
                    if (listFormat.ListType != WdListType.wdListNoNumbering)
                    {
                        isListItem = true;
                        listLevel = listFormat.ListLevelNumber - 1;
                    }
                }
                catch { }

                bool isInTable = false;
                try
                {
                    var info = range.get_Information(WdInformation.wdWithInTable);
                    isInTable = info is bool b && b;
                }
                catch { }

                if (fontSize > 0) fontSizes.Add(fontSize);
                if (!string.IsNullOrEmpty(styleName)) styles.Add(styleName);

                result.Paragraphs.Add(new ParaMeta
                {
                    Index = index,
                    StyleName = styleName,
                    OutlineLevel = outlineLevel,
                    FontSize = fontSize,
                    Bold = bold,
                    Italic = italic,
                    Alignment = alignment,
                    Text = isBlank ? "" : Truncate(text, MaxTextLength),
                    StartPosition = range.Start,
                    IsBlank = isBlank,
                    IsInTable = isInTable,
                    IsListItem = isListItem,
                    ListLevel = listLevel
                });
            }

            result.DistinctFontSizes = fontSizes.OrderByDescending(s => s).ToList();
            result.DistinctStyles = styles.OrderBy(s => s).ToList();

            sw.Stop();
            Debug.WriteLine($"[GraphBuilder] 深度结构提取完成: {result.TotalParagraphs} 段落, 耗时 {sw.ElapsedMilliseconds}ms");

            return result;
        }

        // ═══════════════════════════════════════════════════
        //  内部实现：深度路径 — AST 构建（LLM 标题推断）
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 深度路径：从段落元数据构建文档 AST 根节点。
        /// 收集 OutlineLevel 提示和单行候选段落，交给 LLM 统一裁定标题层级。
        /// </summary>
        private async Task<AstNode> BuildAstAsync(
            DeepStructure rawStructure,
            CancellationToken cancellation = default)
        {
            List<InferredHeading> headings;

            int nonBlankCount = rawStructure.Paragraphs.Count(p => !p.IsBlank);

            if (nonBlankCount <= 3)
            {
                Debug.WriteLine($"[GraphBuilder] 仅 {nonBlankCount} 个非空段落，跳过 LLM 推断，返回平面树");
                headings = new List<InferredHeading>();
            }
            else
            {
                var outlineLevelHints = ExtractOutlineLevelHints(rawStructure);
                Debug.WriteLine($"[GraphBuilder] 检测到 {outlineLevelHints.Count} 个 OutlineLevel 提示");

                var hintedIndices = new HashSet<int>(outlineLevelHints.Select(h => h.ParaIndex));
                var singleLineCandidates = FindSingleLineCandidates(rawStructure, hintedIndices);
                Debug.WriteLine($"[GraphBuilder] 发现 {singleLineCandidates.Count} 个单行候选段落");

                headings = await InferHeadingsWithLlm(
                    rawStructure, outlineLevelHints, singleLineCandidates, cancellation);
            }

            var root = BuildAstTree(rawStructure, headings);
            ComputeAstParaCounts(root, rawStructure.TotalParagraphs);

            return root;
        }

        /// <summary>从 OutlineLevel 提取标题提示</summary>
        private static List<InferredHeading> ExtractOutlineLevelHints(DeepStructure structure)
        {
            var headings = new List<InferredHeading>();

            foreach (var p in structure.Paragraphs)
            {
                if (p.OutlineLevel >= 1 && p.OutlineLevel <= 6 && !p.IsBlank)
                {
                    headings.Add(new InferredHeading
                    {
                        ParaIndex = p.Index,
                        Level = p.OutlineLevel,
                        Title = p.Text
                    });
                }
            }

            return headings;
        }

        /// <summary>找出可能是标题的单行候选段落</summary>
        private static List<int> FindSingleLineCandidates(
            DeepStructure structure, HashSet<int> hintedParaIndices)
        {
            var candidates = new List<int>();

            foreach (var p in structure.Paragraphs)
            {
                if (p.IsBlank || p.IsInTable || p.IsListItem)
                    continue;
                if (hintedParaIndices.Contains(p.Index))
                    continue;
                if (p.Text == null || p.Text.Length > SingleLineCandidateMaxLength || p.Text.Length < 2)
                    continue;

                candidates.Add(p.Index);
            }

            return candidates;
        }

        /// <summary>为单行候选段落构建上下文描述</summary>
        private static string BuildCandidateContext(DeepStructure structure, int paraIndex)
        {
            var sb = new StringBuilder();
            var para = structure.Paragraphs[paraIndex - 1];

            ParaMeta prevPara = null;
            for (int i = paraIndex - 2; i >= 0; i--)
            {
                if (!structure.Paragraphs[i].IsBlank)
                {
                    prevPara = structure.Paragraphs[i];
                    break;
                }
            }

            ParaMeta nextPara = null;
            for (int i = paraIndex; i < structure.Paragraphs.Count; i++)
            {
                if (!structure.Paragraphs[i].IsBlank)
                {
                    nextPara = structure.Paragraphs[i];
                    break;
                }
            }

            if (prevPara != null)
                sb.AppendLine($"  前文: {prevPara.ToDescriptionLine()}");
            else
                sb.AppendLine("  前文: (文档开头)");

            sb.AppendLine($"  ★候选: {para.ToDescriptionLine()}");

            if (nextPara != null)
                sb.AppendLine($"  后文: {nextPara.ToDescriptionLine()}");
            else
                sb.AppendLine("  后文: (文档末尾)");

            return sb.ToString();
        }

        /// <summary>
        /// 将所有证据交给 LLM，一次性返回最终标题列表。
        /// </summary>
        private async Task<List<InferredHeading>> InferHeadingsWithLlm(
            DeepStructure structure,
            List<InferredHeading> outlineLevelHints,
            List<int> singleLineCandidates,
            CancellationToken cancellation)
        {
            string prompt = BuildUnifiedHeadingPrompt(
                structure, outlineLevelHints, singleLineCandidates);

            var request = new SubAgentRequest
            {
                AgentName = "HeadingInference",
                SystemPrompt = "你是一个文档标题层级推断专家。根据提供的段落格式特征（字号、加粗、对齐等）和大纲级别提示，判断哪些段落是标题，以及它们的层级。直接输出结果，不要解释过程。",
                TaskInstruction = prompt,
                MaxRounds = 1,
                ToolDefinitions = null,
                ToolExecutor = null
            };

            var agent = new SubAgent();
            var result = await agent.RunAsync(request, cancellation);

            if (!result.Success)
                throw new InvalidOperationException($"LLM 标题推断失败: {result.Output}");

            var headings = ParseHeadingList(result.Output);

            if (headings.Count == 0)
                Debug.WriteLine("[GraphBuilder] LLM 未返回任何标题，文档可能没有明显的标题结构");
            else
                Debug.WriteLine($"[GraphBuilder] LLM 确定了 {headings.Count} 个标题");

            return headings;
        }

        /// <summary>构建格式特征摘要</summary>
        private static string BuildFormatFeatureSummary(DeepStructure structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"文档名: {structure.DocumentName}");
            sb.AppendLine($"总段落数: {structure.TotalParagraphs}");
            sb.AppendLine($"出现的字号: {string.Join(", ", structure.DistinctFontSizes.Select(s => s + "pt"))}");
            sb.AppendLine($"出现的样式: {string.Join(", ", structure.DistinctStyles)}");

            return sb.ToString();
        }

        /// <summary>构建统一的 LLM 标题推断 Prompt</summary>
        private static string BuildUnifiedHeadingPrompt(
            DeepStructure structure,
            List<InferredHeading> outlineLevelHints,
            List<int> singleLineCandidates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个文档结构分析专家。请分析以下 Word 文档的段落信息，确定哪些段落是标题以及它们的层级。");
            sb.AppendLine();

            sb.AppendLine("=== 1. 文档格式概况 ===");
            sb.Append(BuildFormatFeatureSummary(structure));
            sb.AppendLine();

            sb.AppendLine("=== 2. 大纲级别提示（Word 样式中已标记的标题）===");
            if (outlineLevelHints.Count > 0)
            {
                sb.AppendLine("以下段落在 Word 中设置了标题样式，供参考（你可以调整层级或排除误标）：");
                foreach (var h in outlineLevelHints)
                    sb.AppendLine($"  段落 #{h.ParaIndex}, 样式层级={h.Level}, 文本: {h.Title}");
            }
            else
            {
                sb.AppendLine("（无，文档未使用标准标题样式）");
            }
            sb.AppendLine();

            sb.AppendLine("=== 3. 疑似标题段落（短段落，可能是标题但未被样式标记）===");
            if (singleLineCandidates.Count > 0)
            {
                sb.AppendLine("以下短段落可能是标题，每个附带前后各 1 段上下文：");
                sb.AppendLine();
                foreach (int paraIndex in singleLineCandidates)
                {
                    sb.AppendLine($"--- 候选 #{paraIndex} ---");
                    sb.Append(BuildCandidateContext(structure, paraIndex));
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("（无单行候选段落）");
            }
            sb.AppendLine();

            sb.AppendLine($"=== 4. 段落格式采样（前 {MaxSampleParagraphs} 个非空段落）===");
            int count = 0;
            foreach (var p in structure.Paragraphs)
            {
                if (p.IsBlank) continue;
                sb.AppendLine(p.ToDescriptionLine());
                count++;
                if (count >= MaxSampleParagraphs) break;
            }
            sb.AppendLine();

            sb.AppendLine("=== 判断要求 ===");
            sb.AppendLine("综合考虑以上所有材料，确定文档的标题及其层级（1-6）：");
            sb.AppendLine("- 大纲级别提示是重要参考，但你可以根据实际情况调整或补充");
            sb.AppendLine("- 短段落如果独占一行、前后是长正文段落、且语义像标题名称，则很可能是标题");
            sb.AppendLine("- 标题通常是名词短语或简短描述，通常不以句号结尾");
            sb.AppendLine("- 字号越大、加粗、居中通常意味着越高层级");
            sb.AppendLine("- 层级应体现文档的逻辑嵌套关系");
            sb.AppendLine("- 只输出你有信心判断为标题的段落");
            sb.AppendLine();
            sb.AppendLine("请严格按以下 JSON 格式输出（不要输出任何其他内容）：");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            sb.AppendLine("  {\"para_index\": 3, \"level\": 1, \"title\": \"第一章 引言\"},");
            sb.AppendLine("  {\"para_index\": 15, \"level\": 2, \"title\": \"1.1 研究背景\"}");
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("字段说明：");
            sb.AppendLine("- para_index: 段落序号（与上面采样中的段落 # 一致）");
            sb.AppendLine("- level: 1-6，标题层级");
            sb.AppendLine("- title: 标题文本");

            return sb.ToString();
        }

        /// <summary>从 LLM 输出中解析标题列表 JSON</summary>
        private static List<InferredHeading> ParseHeadingList(string llmOutput)
        {
            string json = ExtractJsonArray(llmOutput);
            if (string.IsNullOrEmpty(json))
                return new List<InferredHeading>();

            JArray arr;
            try
            {
                arr = JArray.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Debug.WriteLine($"[GraphBuilder] LLM 输出的 JSON 解析失败: {ex.Message}");
                Debug.WriteLine($"[GraphBuilder] 提取到的疑似 JSON: {json.Substring(0, Math.Min(200, json.Length))}");
                throw new InvalidOperationException(
                    $"LLM 返回的内容无法解析为 JSON 数组。\n" +
                    $"提取到的内容: {json.Substring(0, Math.Min(200, json.Length))}\n" +
                    $"LLM 原始输出: {llmOutput.Substring(0, Math.Min(500, llmOutput.Length))}", ex);
            }

            var headings = new List<InferredHeading>();
            var seen = new HashSet<int>();

            foreach (var item in arr)
            {
                int paraIndex = item["para_index"]?.Value<int>() ?? 0;
                int level = item["level"]?.Value<int>() ?? 0;
                string title = item["title"]?.Value<string>();

                if (paraIndex <= 0 || level < 1 || level > 6)
                    continue;

                if (!seen.Add(paraIndex))
                    continue;

                headings.Add(new InferredHeading
                {
                    ParaIndex = paraIndex,
                    Level = level,
                    Title = title ?? ""
                });
            }

            headings.Sort((a, b) => a.ParaIndex.CompareTo(b.ParaIndex));

            return headings;
        }

        /// <summary>从 LLM 文本输出中提取 JSON 数组</summary>
        private static string ExtractJsonArray(string text)
        {
            int codeStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (codeStart >= 0)
            {
                int codeEnd = text.IndexOf("```", codeStart + 7);
                if (codeEnd < 0) codeEnd = text.Length;
                string codeBlock = text.Substring(codeStart + 7, codeEnd - codeStart - 7);
                int start = codeBlock.IndexOf('[');
                int end = codeBlock.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    string candidate = codeBlock.Substring(start, end - start + 1);
                    if (LooksLikeJsonArray(candidate))
                        return candidate;
                }
            }

            int firstBracket = text.IndexOf('[');
            int lastBracket = text.LastIndexOf(']');
            if (firstBracket >= 0 && lastBracket > firstBracket)
            {
                string candidate = text.Substring(firstBracket, lastBracket - firstBracket + 1);
                if (LooksLikeJsonArray(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>快速检查字符串是否像一个 JSON 数组</summary>
        private static bool LooksLikeJsonArray(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '[') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == '{' || c == '[' || c == '"' || c == '-'
                    || char.IsDigit(c) || c == 't' || c == 'f' || c == 'n' || c == ']';
            }
            return false;
        }

        /// <summary>从已标记的标题列表构建 AST 树</summary>
        private static AstNode BuildAstTree(
            DeepStructure structure, List<InferredHeading> headings)
        {
            var root = new AstNode
            {
                NodeId = "root",
                Type = AstNodeType.Root,
                Level = 0,
                Title = structure.DocumentName,
                ParaStart = 1,
                ParaEnd = structure.TotalParagraphs + 1,
                CharStart = structure.Paragraphs.Count > 0
                    ? structure.Paragraphs[0].StartPosition : 0,
                CharEnd = -1
            };

            if (headings.Count == 0)
                return root;

            var stack = new Stack<AstNode>();
            stack.Push(root);

            for (int i = 0; i < headings.Count; i++)
            {
                var h = headings[i];
                var para = structure.Paragraphs[h.ParaIndex - 1];

                int nextParaIndex = (i + 1 < headings.Count)
                    ? headings[i + 1].ParaIndex
                    : structure.TotalParagraphs + 1;

                int charEnd;
                if (nextParaIndex <= structure.TotalParagraphs)
                    charEnd = structure.Paragraphs[nextParaIndex - 1].StartPosition;
                else
                    charEnd = -1;

                var node = new AstNode
                {
                    NodeId = ComputeAstNodeId(h.Level, h.Title, h.ParaIndex),
                    Type = AstNodeType.Section,
                    Level = h.Level,
                    Title = h.Title,
                    ParaStart = h.ParaIndex,
                    ParaEnd = nextParaIndex,
                    CharStart = para.StartPosition,
                    CharEnd = charEnd,
                    ContentPreview = BuildAstContentPreview(structure, h.ParaIndex, nextParaIndex)
                };

                while (stack.Count > 1 && stack.Peek().Level >= h.Level)
                    stack.Pop();

                stack.Peek().Children.Add(node);
                stack.Push(node);
            }

            return root;
        }

        /// <summary>生成节点内容预览</summary>
        private static string BuildAstContentPreview(
            DeepStructure structure, int paraStart, int paraEnd)
        {
            for (int idx = paraStart + 1; idx < paraEnd && idx <= structure.TotalParagraphs; idx++)
            {
                var p = structure.Paragraphs[idx - 1];
                if (!p.IsBlank)
                {
                    string text = p.Text;
                    if (text.Length > 80)
                        text = text.Substring(0, 80) + "…";
                    return text;
                }
            }

            return null;
        }

        /// <summary>递归计算每个 AST 节点的段落统计</summary>
        private static void ComputeAstParaCounts(AstNode node, int totalParagraphs)
        {
            if (node.Type == AstNodeType.Root)
            {
                node.TotalParaCount = totalParagraphs;
                node.DirectParaCount = 0;
                foreach (var child in node.Children)
                    ComputeAstParaCounts(child, totalParagraphs);
                return;
            }

            int totalChildParas = 0;
            foreach (var child in node.Children)
            {
                ComputeAstParaCounts(child, totalParagraphs);
                totalChildParas += child.TotalParaCount;
            }

            node.TotalParaCount = node.ParaEnd - node.ParaStart;
            node.DirectParaCount = node.TotalParaCount - totalChildParas;
        }

        /// <summary>从 AST 树中收集标题信息（深度路径用）</summary>
        private static List<HeadingEntry> CollectHeadingsFromAst(AstNode astRoot)
        {
            var headings = new List<HeadingEntry>();
            CollectHeadingsRecursive(astRoot, headings);
            headings.Sort((a, b) => a.ParaIndex.CompareTo(b.ParaIndex));
            return headings;
        }

        private static void CollectHeadingsRecursive(AstNode node, List<HeadingEntry> headings)
        {
            if (node.Type == AstNodeType.Section)
            {
                headings.Add(new HeadingEntry
                {
                    ParaIndex = node.ParaStart,
                    Level = node.Level,
                    Title = node.Title,
                    RangeStart = node.CharStart,
                    RangeEnd = -1
                });
            }

            foreach (var child in node.Children)
                CollectHeadingsRecursive(child, headings);
        }

        /// <summary>生成 AST 节点 ID：基于 level + title + paraIndex 的 MD5 前 8 位。</summary>
        private static string ComputeAstNodeId(int level, string title, int paraIndex)
        {
            string input = $"{level}:{title}:{paraIndex}";
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }

        // ═══════════════════════════════════════════════════
        //  通用工具
        // ═══════════════════════════════════════════════════

        private static string Truncate(string text, int maxLen)
        {
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen) + "…";
        }

        // ═══════════════════════════════════════════════════
        //  内部数据类型
        // ═══════════════════════════════════════════════════

        private class HeadingEntry
        {
            public int ParaIndex { get; set; }
            public int Level { get; set; }
            public string Title { get; set; }
            public int RangeStart { get; set; }
            public int RangeEnd { get; set; }
        }

        private class InferredHeading
        {
            public int ParaIndex { get; set; }
            public int Level { get; set; }
            public string Title { get; set; }
        }

        /// <summary>AST 节点类型（仅深度路径内部使用）</summary>
        private enum AstNodeType
        {
            Root,
            Section,
        }

        /// <summary>AST 节点（仅深度路径内部使用）</summary>
        private class AstNode
        {
            public string NodeId { get; set; }
            public AstNodeType Type { get; set; }
            public int Level { get; set; }
            public string Title { get; set; }
            public int ParaStart { get; set; }
            public int ParaEnd { get; set; }
            public int CharStart { get; set; }
            public int CharEnd { get; set; }
            public int TotalParaCount { get; set; }
            public int DirectParaCount { get; set; }
            public string ContentPreview { get; set; }
            public List<AstNode> Children { get; set; } = new List<AstNode>();
        }

        /// <summary>单个段落的结构描述（深度模式格式元数据）</summary>
        private class ParaMeta
        {
            public int Index { get; set; }
            public string StyleName { get; set; }
            public int OutlineLevel { get; set; }
            public float FontSize { get; set; }
            public bool? Bold { get; set; }
            public bool? Italic { get; set; }
            public string Alignment { get; set; }
            public string Text { get; set; }
            public int StartPosition { get; set; }
            public bool IsBlank { get; set; }
            public bool IsInTable { get; set; }
            public bool IsListItem { get; set; }
            public int ListLevel { get; set; }

            /// <summary>格式化为单行描述</summary>
            public string ToDescriptionLine()
            {
                var tags = new List<string>();

                var fontParts = new List<string>();
                if (FontSize > 0) fontParts.Add($"{FontSize}pt");
                if (Bold == true) fontParts.Add("加粗");
                if (Italic == true) fontParts.Add("斜体");
                if (!string.IsNullOrEmpty(Alignment)) fontParts.Add(Alignment);
                if (fontParts.Count > 0)
                    tags.Add($"字体:{string.Join(", ", fontParts)}");

                if (!string.IsNullOrEmpty(StyleName))
                    tags.Add($"样式:{StyleName}");

                if (OutlineLevel >= 1 && OutlineLevel <= 9)
                    tags.Add($"大纲:{OutlineLevel}级");

                if (IsListItem)
                    tags.Add($"列表:L{ListLevel}");

                if (IsInTable)
                    tags.Add("表格内");

                string tagStr = tags.Count > 0 ? $" [{string.Join("] [", tags)}]" : "";
                string textStr = IsBlank ? "(空行)" : Text;

                return $"[段落 #{Index}]{tagStr} {textStr}";
            }
        }

        /// <summary>深度结构提取结果</summary>
        private class DeepStructure
        {
            public string DocumentName { get; set; }
            public int TotalParagraphs { get; set; }
            public List<ParaMeta> Paragraphs { get; set; } = new List<ParaMeta>();
            public List<float> DistinctFontSizes { get; set; } = new List<float>();
            public List<string> DistinctStyles { get; set; } = new List<string>();
        }
    }
}
