using FuXing.SubAgents;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.Core
{
    // ═══════════════════════════════════════════════════════════════
    //  文档图构建器
    //
    //  职责：
    //  1. 扫描文档段落，识别标题 → 创建 Section 节点
    //  2. 一步到位发现所有内容元素（Heading/TextBlock/Table/Image/List/Preamble）
    //  3. 为每个节点创建 CC 锚点（通过 AnchorManager）
    //  4. 建立 parent/child/prev/next 关系
    //
    //  双路径：
    //  - 快速路径：从大纲级别程序化构建（无 LLM）
    //  - 深度路径：全量段落提取 + LLM 推断标题（不规范文档）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档图构建器。扫描文档结构，创建 CC 锚定的节点图。
    /// </summary>
    public class DocumentGraphBuilder
    {
        private readonly AnchorManager _anchors;
        private int _sectionCounter;
        private int _headingCounter;
        private int _preambleCounter;
        private int _tableCounter;
        private int _imageCounter;
        private int _textBlockCounter;
        private int _listCounter;
        private int _paragraphCounter;

        public DocumentGraphBuilder(AnchorManager anchors)
        {
            _anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
        }

        // ═══════════════════════════════════════════════════
        //  快速路径：从大纲级别构建完整文档图
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 从大纲级别构建完整文档图（Section + Heading + 内容元素一步到位）。
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

            // 第一个标题之前的内容 → Preamble 节点
            BuildPreamble(doc, graph, root, headings);

            if (headings.Count > 0)
            {
                BuildSectionHierarchy(doc, graph, root, headings);
                BuildSiblingLinks(graph, root);

                // 为每个 Section 立即发现内容元素（Heading + TextBlock + Table + Image + List）
                DiscoverAllSectionContents(doc, graph);
            }

            sw.Stop();
            DebugLogger.Instance.LogDebug("GraphBuilder", $"文档图构建完成: {graph.Index.Count} 个节点, 耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        // ═══════════════════════════════════════════════════
        //  深度路径：LLM 推断标题
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 深度路径：全量段落提取 + LLM 推断标题，适用于不规范文档。
        /// </summary>
        public async Task<DocumentGraph> BuildFullDeepAsync(
            Document doc, CancellationToken cancellation = default)
        {
            var sw = Stopwatch.StartNew();
            ResetCounters();

            var rawStructure = DocumentStructureExtractor.Extract(doc);
            var astBuilder = new DocumentAstBuilder();
            var astRoot = await astBuilder.BuildAsync(rawStructure, cancellation);

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
            DebugLogger.Instance.LogDebug("GraphBuilder", $"深度文档图构建完成: {graph.Index.Count} 个节点, 耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        // ═══════════════════════════════════════════════════
        //  Section 已在 map 时完整展开，ExpandSection 仅做兼容
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Section 节点已在构建时自动展开，此方法仅标记 Expanded 状态。
        /// </summary>
        public void ExpandSection(Document doc, DocumentGraph graph, string sectionNodeId)
        {
            var section = graph.GetById(sectionNodeId);
            if (section == null)
                throw new InvalidOperationException($"节点不存在: {sectionNodeId}");
            if (section.Type != DocNodeType.Section)
                throw new InvalidOperationException($"只能展开 Section 节点，当前类型: {section.Type}");
            // Section 已在 map 时完整展开，无需额外操作
        }

        // ═══════════════════════════════════════════════════
        //  展开 TextBlock 到段落级
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 展开一个 TextBlock 节点，为其中每个段落创建 Paragraph 子节点。
        /// 行为与 ExpandSection 对称：批量创建 CC 锚定的 L3 段落节点。
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

                // 段落需在 TextBlock CC 范围内
                if (ps < blockStart || ps >= blockEnd) continue;

                string text = paraRange.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                paraIdx++;
                string nodeId = NextId("p");
                string anchorLabel = $"map:{nodeId}";

                var node = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Paragraph,
                    Title = $"段落{paraIdx}",
                    Preview = text.Length > 100 ? text.Substring(0, 100) + "…" : text,
                    AnchorLabel = anchorLabel,
                    ParentId = block.Id,
                    Meta = new Dictionary<string, string>
                    {
                        ["range_start"] = ps.ToString(),
                        ["range_end"] = pe.ToString()
                    }
                };

                if (_anchors.TryPlace(doc, paraRange, anchorLabel) == null)
                    node.AnchorLabel = null;
                graph.AddNode(node);
                paraNodes.Add(node);
            }

            // 设置子节点列表和兄弟链
            block.ChildIds = paraNodes.Select(n => n.Id).ToList();
            RebuildSiblingLinks(graph, block);

            block.Expanded = true;

            DebugLogger.Instance.LogDebug("GraphBuilder", $"展开 TextBlock {textBlockNodeId}: 发现 {paraNodes.Count} 个段落");
        }

        // ═══════════════════════════════════════════════════
        //  内部实现
        // ═══════════════════════════════════════════════════
        /// <summary>优先用 CC 锚点定位，锚点不存在时回退到节点 Meta 中存储的字符偏移</summary>
        private Range GetNodeRange(Document doc, DocNode node)
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
            throw new InvalidOperationException(
                $"无法定位节点 [{node.Id}] {node.Title}：CC 锚点丢失且无位置元数据");
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

        /// <summary>从 AST 树中收集标题信息（深度路径用）</summary>
        private static List<HeadingEntry> CollectHeadingsFromAst(DocumentAstNode astRoot)
        {
            var headings = new List<HeadingEntry>();
            CollectHeadingsRecursive(astRoot, headings);
            headings.Sort((a, b) => a.ParaIndex.CompareTo(b.ParaIndex));
            return headings;
        }

        private static void CollectHeadingsRecursive(DocumentAstNode node, List<HeadingEntry> headings)
        {
            if (node.Type == AstNodeType.Section)
            {
                headings.Add(new HeadingEntry
                {
                    ParaIndex = node.ParaStart,
                    Level = node.Level,
                    Title = node.Title,
                    RangeStart = node.CharStart,
                    RangeEnd = -1 // 将从文档中重算
                });
            }

            foreach (var child in node.Children)
                CollectHeadingsRecursive(child, headings);
        }

        /// <summary>用栈算法构建 Section 层级关系 + 创建 CC 锚点</summary>
        private void BuildSectionHierarchy(
            Document doc, DocumentGraph graph, DocNode root, List<HeadingEntry> headings)
        {
            var stack = new Stack<DocNode>();
            stack.Push(root);

            for (int i = 0; i < headings.Count; i++)
            {
                var h = headings[i];

                // 从段落动态获取当前字符位置（CC 创建会插入控制字符导致偏移漂移，
                // 因此不能使用 ExtractHeadings 时快照的 RangeStart 整数）
                var headingPara = doc.Paragraphs[h.ParaIndex];
                int headingStart = headingPara.Range.Start;
                int bodyStart = headingPara.Range.End;
                int rangeEnd;
                if (i + 1 < headings.Count)
                    rangeEnd = doc.Paragraphs[headings[i + 1].ParaIndex].Range.Start;
                else
                    rangeEnd = doc.Content.End - 1;

                // 创建节点
                string nodeId = NextId("s");
                string anchorLabel = $"map:{nodeId}";

                var sectionNode = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Section,
                    Title = h.Title,
                    Level = h.Level,
                    AnchorLabel = anchorLabel,
                    Meta = new Dictionary<string, string>
                    {
                        ["heading_start"] = headingStart.ToString(),
                        ["range_start"] = bodyStart.ToString(),
                        ["range_end"] = rangeEnd.ToString()
                    }
                };

                // CC 锚点仅覆盖正文区域（标题段落之后），编辑操作天然不会破坏标题
                var bodyRange = doc.Range(bodyStart, rangeEnd);
                if (_anchors.TryPlace(doc, bodyRange, anchorLabel) == null)
                {
                    sectionNode.AnchorLabel = null;
                    DebugLogger.Instance.LogDebug("GraphBuilder", $"节点 {nodeId} ({h.Title}) 无法创建 CC 锚点，使用位置回退, range=[{bodyStart},{rangeEnd})");
                }

                // 读取内容预览（正文第一行非空文本）
                sectionNode.Preview = ExtractBodyPreview(bodyRange);

                // 退栈到合适的父节点
                while (stack.Count > 1 && stack.Peek().Level >= h.Level)
                    stack.Pop();

                // 建立父子关系
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

                // 递归处理子节点
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
                Level = 0,
                AnchorLabel = null
            };
        }

        /// <summary>
        /// 为文档第一个标题之前的内容创建 Preamble 节点。
        /// 如果第一个标题前有非空正文内容，则创建 Preamble 节点覆盖该区域。
        /// </summary>
        private void BuildPreamble(Document doc, DocumentGraph graph, DocNode root, List<HeadingEntry> headings)
        {
            int docStart = doc.Content.Start;
            int firstHeadingStart;

            if (headings.Count > 0)
            {
                // 重新从文档获取精确位置（CC 创建前）
                firstHeadingStart = doc.Paragraphs[headings[0].ParaIndex].Range.Start;
            }
            else
            {
                // 无标题 → 整个文档作为 Preamble
                firstHeadingStart = doc.Content.End - 1;
            }

            if (firstHeadingStart <= docStart) return;

            // 检查是否有非空文本
            var preambleRange = doc.Range(docStart, firstHeadingStart);
            string text = preambleRange.Text ?? "";
            if (string.IsNullOrWhiteSpace(text.TrimEnd('\r', '\n', '\a'))) return;

            string nodeId = NextId("pre");
            string anchorLabel = $"map:{nodeId}";

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.Preamble,
                Title = "前言",
                Preview = ExtractBodyPreview(preambleRange),
                AnchorLabel = anchorLabel,
                ParentId = root.Id,
                Level = 0,
                Meta = new Dictionary<string, string>
                {
                    ["range_start"] = docStart.ToString(),
                    ["range_end"] = firstHeadingStart.ToString()
                }
            };

            if (_anchors.TryPlace(doc, preambleRange, anchorLabel) == null)
                node.AnchorLabel = null;

            graph.AddNode(node);
            root.ChildIds.Insert(0, nodeId);
        }

        /// <summary>
        /// 遍历所有 Section 节点，为每个 Section 创建 Heading 子节点并发现内容元素。
        /// </summary>
        private void DiscoverAllSectionContents(Document doc, DocumentGraph graph)
        {
            var sections = graph.FindByType(DocNodeType.Section);
            foreach (var section in sections)
            {
                // 1. 创建 Heading 子节点
                CreateHeadingNode(doc, graph, section);

                // 2. 发现直属内容元素（TextBlock / Table / Image / List）
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

            // 重建所有兄弟链（因为子节点列表已变更）
            BuildSiblingLinks(graph, graph.Root);
        }

        /// <summary>为 Section 节点创建对应的 Heading 子节点（CC 仅覆盖标题段落）</summary>
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
            string anchorLabel = $"map:{nodeId}";

            var headingRange = doc.Range(headingStart, headingEnd);

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.Heading,
                Title = section.Title,
                AnchorLabel = anchorLabel,
                ParentId = section.Id,
                Level = section.Level,
                Meta = new Dictionary<string, string>
                {
                    ["range_start"] = headingStart.ToString(),
                    ["range_end"] = headingEnd.ToString()
                }
            };

            if (_anchors.TryPlace(doc, headingRange, anchorLabel) == null)
                node.AnchorLabel = null;

            graph.AddNode(node);
            // Heading 始终是 Section 的第一个子节点
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
            // 收集该范围内子 Section 的范围（要排除的区域）
            var childSectionRanges = new List<(int start, int end)>();
            foreach (var childId in parentSection.ChildIds)
            {
                var child = graph.GetById(childId);
                if (child?.Type == DocNodeType.Section && child.AnchorLabel != null)
                {
                    try
                    {
                        var cr = _anchors.GetRange(doc, child.AnchorLabel);
                        childSectionRanges.Add((cr.Start, cr.End));
                    }
                    catch { }
                }
            }

            // 跳过标题段本身，扫描直属内容段落
            var textBlockParas = new List<Paragraph>();
            var listParas = new List<Paragraph>();
            bool inList = false;

            foreach (Paragraph para in doc.Paragraphs)
            {
                var paraRange = para.Range;
                int ps = paraRange.Start;

                // 不在 section 范围内则跳过
                if (ps < sectionStart || ps >= sectionEnd) continue;

                // 跳过子 Section 范围内的段落
                bool inChildSection = false;
                foreach (var (cs, ce) in childSectionRanges)
                {
                    if (ps >= cs && ps < ce) { inChildSection = true; break; }
                }
                if (inChildSection) continue;

                // 是标题段本身？跳过
                int outlineLevel;
                try { outlineLevel = (int)para.OutlineLevel; }
                catch { outlineLevel = 10; }
                if (outlineLevel >= 1 && outlineLevel <= 9) continue;

                // 检测表格
                bool isInTable = false;
                try
                {
                    var info = paraRange.get_Information(WdInformation.wdWithInTable);
                    isInTable = info is bool b && b;
                }
                catch { }

                if (isInTable)
                {
                    // 表格由 doc.Tables 统一处理，这里跳过表格内段落
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                    continue;
                }

                // 检测图片
                bool hasImage = false;
                try { hasImage = paraRange.InlineShapes.Count > 0; }
                catch { }

                if (hasImage)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;

                    // 创建 Image 节点
                    var imgNode = CreateImageNode(doc, graph, parentSection, paraRange);
                    if (imgNode != null) outContentNodes.Add(imgNode);
                    continue;
                }

                // 检测列表
                bool isList = false;
                try
                {
                    isList = para.Range.ListFormat.ListType !=
                             WdListType.wdListNoNumbering;
                }
                catch { }

                if (isList)
                {
                    FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
                    listParas.Add(para);
                    inList = true;
                    continue;
                }

                // 普通文本段落
                if (inList)
                {
                    FlushList(doc, graph, parentSection, listParas, outContentNodes);
                    inList = false;
                }

                string text = paraRange.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                    textBlockParas.Add(para);
            }

            // 刷新剩余
            FlushTextBlock(doc, graph, parentSection, textBlockParas, outContentNodes);
            FlushList(doc, graph, parentSection, listParas, outContentNodes);

            // 发现 section 范围内的表格
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
            string anchorLabel = $"map:{nodeId}";

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.TextBlock,
                Title = $"文本块 ({paras.Count}段)",
                Preview = firstText.Length > 100 ? firstText.Substring(0, 100) + "…" : firstText,
                AnchorLabel = anchorLabel,
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["para_count"] = paras.Count.ToString(),
                    ["range_start"] = start.ToString(),
                    ["range_end"] = end.ToString()
                }
            };

            if (_anchors.TryPlace(doc, doc.Range(start, end), anchorLabel) == null)
                node.AnchorLabel = null;
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
            string anchorLabel = $"map:{nodeId}";

            var node = new DocNode
            {
                Id = nodeId,
                Type = DocNodeType.List,
                Title = $"列表 ({paras.Count}项)",
                Preview = firstText.Length > 100 ? firstText.Substring(0, 100) + "…" : firstText,
                AnchorLabel = anchorLabel,
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["item_count"] = paras.Count.ToString(),
                    ["range_start"] = start.ToString(),
                    ["range_end"] = end.ToString()
                }
            };

            if (_anchors.TryPlace(doc, doc.Range(start, end), anchorLabel) == null)
                node.AnchorLabel = null;
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
            string anchorLabel = $"map:{nodeId}";

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
                AnchorLabel = anchorLabel,
                ParentId = parent.Id,
                Meta = new Dictionary<string, string>
                {
                    ["width_cm"] = widthCm.ToString("F1"),
                    ["height_cm"] = heightCm.ToString("F1"),
                    ["range_start"] = paraRange.Start.ToString(),
                    ["range_end"] = paraRange.End.ToString()
                }
            };

            if (_anchors.TryPlace(doc, paraRange, anchorLabel) == null)
                node.AnchorLabel = null;
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

                // 排除子 Section 中的表格
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
                string anchorLabel = $"map:{nodeId}";

                var node = new DocNode
                {
                    Id = nodeId,
                    Type = DocNodeType.Table,
                    Title = $"表{_tableCounter} ({rows}×{cols})",
                    AnchorLabel = anchorLabel,
                    ParentId = parent.Id,
                    Meta = new Dictionary<string, string>
                    {
                        ["rows"] = rows.ToString(),
                        ["cols"] = cols.ToString(),
                        ["range_start"] = tableStart.ToString(),
                        ["range_end"] = table.Range.End.ToString()
                    }
                };

                // 表格第一个单元格的文本作为预览
                try
                {
                    string cellText = table.Cell(1, 1).Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
                    if (cellText.Length > 60) cellText = cellText.Substring(0, 60) + "…";
                    node.Preview = cellText;
                }
                catch { }

                if (_anchors.TryPlace(doc, table.Range, anchorLabel) == null)
                    node.AnchorLabel = null;
                graph.AddNode(node);
                outNodes.Add(node);
            }
        }

        /// <summary>将新发现的内容节点按文档顺序合并到 section 的子节点列表中</summary>
        private void MergeContentNodesIntoSection(
            Document doc, DocumentGraph graph, DocNode section, List<DocNode> contentNodes)
        {
            if (contentNodes.Count == 0) return;

            // 获取所有子节点及其文档位置，统一排序
            var allChildren = new List<(string id, int position)>();

            // 已有子节点（Section 类型）—— 从 CC 获取真实位置
            foreach (var childId in section.ChildIds)
            {
                var child = graph.GetById(childId);
                if (child == null) continue;

                int pos;
                if (child.AnchorLabel != null)
                {
                    try
                    {
                        var ccRange = _anchors.GetRange(doc, child.AnchorLabel);
                        pos = ccRange.Start;
                    }
                    catch
                    {
                        // CC 不存在时用 Meta 回退
                        pos = child.Meta != null && child.Meta.TryGetValue("range_start", out var rs2)
                            ? int.Parse(rs2) : int.MaxValue;
                    }
                }
                else
                {
                    pos = child.Meta != null && child.Meta.TryGetValue("range_start", out var rs3)
                        ? int.Parse(rs3) : int.MaxValue;
                }
                allChildren.Add((childId, pos));
            }

            // 新发现的内容节点
            foreach (var node in contentNodes)
            {
                int pos = node.Meta != null && node.Meta.TryGetValue("range_start", out var rs)
                    ? int.Parse(rs) : int.MaxValue;
                allChildren.Add((node.Id, pos));
            }

            // 按文档位置排序
            allChildren.Sort((a, b) => a.position.CompareTo(b.position));

            // 更新 section 的子节点列表
            section.ChildIds = allChildren.Select(c => c.id).ToList();
        }

        // ═══════════════════════════════════════════════════
        //  内部数据结构
        // ═══════════════════════════════════════════════════

        private class HeadingEntry
        {
            public int ParaIndex { get; set; }
            public int Level { get; set; }
            public string Title { get; set; }
            public int RangeStart { get; set; }
            public int RangeEnd { get; set; }
        }
    }
}
