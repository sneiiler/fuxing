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
    //  1. 扫描文档段落，识别标题 → 创建 Section 节点（L1 骨架层）
    //  2. 为每个节点创建 CC 锚点（通过 AnchorManager）
    //  3. 建立 parent/child/prev/next 关系
    //  4. 按需展开 Section 内部（L2 内容层：Table/Image/TextBlock/List）
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
        //  L1 快速路径：从大纲级别构建骨架
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 从大纲级别快速构建文档图骨架（L1 层）。
        /// 只包含标题 Section 节点，每个节点绑定一个 CC 锚点。
        /// </summary>
        public DocumentGraph BuildSkeleton(Document doc)
        {
            var sw = Stopwatch.StartNew();
            ResetCounters();

            var graph = new DocumentGraph
            {
                DocumentName = doc.Name,
                IsDeepPerception = false,
                BuiltAt = DateTime.Now
            };

            // 根节点（不创建 CC，代表整个文档）
            var root = new DocNode
            {
                Id = "doc",
                Type = DocNodeType.Document,
                Title = doc.Name,
                Level = 0,
                AnchorLabel = null // 根节点不需要 CC
            };
            graph.Root = root;
            graph.AddNode(root);

            // 扫描段落，提取标题
            var headings = ExtractHeadings(doc);

            if (headings.Count == 0)
            {
                sw.Stop();
                Debug.WriteLine($"[GraphBuilder] 骨架构建完成: 0 个标题, 耗时 {sw.ElapsedMilliseconds}ms");
                return graph;
            }

            // 用栈算法构建层级关系 + 创建 CC 锚点
            BuildSectionHierarchy(doc, graph, root, headings);

            // 建立兄弟链（prev/next）
            BuildSiblingLinks(graph, root);

            sw.Stop();
            Debug.WriteLine($"[GraphBuilder] 骨架构建完成: {headings.Count} 个节点, " +
                            $"耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        // ═══════════════════════════════════════════════════
        //  L1 深度路径：LLM 推断标题
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 深度路径：全量段落提取 + LLM 推断标题，适用于不规范文档。
        /// 核心标题推断逻辑委托给 DocumentAstBuilder（复用已有的 LLM 推断）后
        /// 转化为图结构。
        /// </summary>
        public async Task<DocumentGraph> BuildSkeletonDeepAsync(
            Document doc, CancellationToken cancellation = default)
        {
            var sw = Stopwatch.StartNew();
            ResetCounters();

            // 使用已有的 DocumentStructureExtractor 提取段落元数据
            var rawStructure = DocumentStructureExtractor.Extract(doc);

            // 使用已有的 AstBuilder 推断标题
            var astBuilder = new DocumentAstBuilder();
            var astRoot = await astBuilder.BuildAsync(rawStructure, cancellation);

            // 将 AST 树转化为图
            var graph = new DocumentGraph
            {
                DocumentName = doc.Name,
                IsDeepPerception = true,
                BuiltAt = DateTime.Now
            };

            var root = new DocNode
            {
                Id = "doc",
                Type = DocNodeType.Document,
                Title = doc.Name,
                Level = 0,
                AnchorLabel = null
            };
            graph.Root = root;
            graph.AddNode(root);

            // 从 AST 树收集标题信息，然后走同样的骨架构建流程
            var headings = CollectHeadingsFromAst(astRoot);

            if (headings.Count > 0)
            {
                BuildSectionHierarchy(doc, graph, root, headings);
                BuildSiblingLinks(graph, root);
            }

            sw.Stop();
            Debug.WriteLine($"[GraphBuilder] 深度骨架构建完成: {headings.Count} 个节点, " +
                            $"耗时 {sw.ElapsedMilliseconds}ms");

            return graph;
        }

        // ═══════════════════════════════════════════════════
        //  L2 展开：发现 Section 内部元素
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// 展开一个 Section 节点的内部内容，发现其中的 Table/Image/TextBlock/List。
        /// 新发现的节点作为该 Section 的子节点添加，各自创建 CC 锚点。
        /// </summary>
        public void ExpandSection(Document doc, DocumentGraph graph, string sectionNodeId)
        {
            var section = graph.GetById(sectionNodeId);
            if (section == null)
                throw new InvalidOperationException($"节点不存在: {sectionNodeId}");
            if (section.Type != DocNodeType.Section)
                throw new InvalidOperationException($"只能展开 Section 节点，当前类型: {section.Type}");
            if (section.Expanded)
                return; // 已展开，无需重复

            var sectionRange = GetNodeRange(doc, section);
            int sectionStart = sectionRange.Start;
            int sectionEnd = sectionRange.End;

            // 已有的子 Section 的 ID 集合（不要覆盖）
            var existingChildIds = new HashSet<string>(section.ChildIds);

            // 扫描 section 范围内的段落，识别内容元素
            var contentNodes = new List<DocNode>();
            DiscoverContentElements(doc, graph, sectionStart, sectionEnd,
                section, existingChildIds, contentNodes);

            // 将新节点插入到子节点列表（在已有子 Section 之间穿插）
            // 策略：按文档顺序合并
            MergeContentNodesIntoSection(doc, graph, section, contentNodes);

            // 重新建立该 section 下的兄弟链
            RebuildSiblingLinks(graph, section);

            section.Expanded = true;

            Debug.WriteLine($"[GraphBuilder] 展开 {sectionNodeId}: " +
                            $"发现 {contentNodes.Count} 个内容元素");
        }

        // ═══════════════════════════════════════════════════
        //  L3 展开：发现 TextBlock 内部段落
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

            Debug.WriteLine($"[GraphBuilder] 展开 TextBlock {textBlockNodeId}: " +
                            $"发现 {paraNodes.Count} 个段落");
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

                // 计算该 Section 的范围：从当前标题开始，到下一个同级或更高标题之前
                int rangeStart = h.RangeStart;
                int rangeEnd;
                if (i + 1 < headings.Count)
                    rangeEnd = headings[i + 1].RangeStart;
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
                        ["range_start"] = rangeStart.ToString(),
                        ["range_end"] = rangeEnd.ToString()
                    }
                };

                // 创建 CC 锚点（文档已有 ContentControl 时可能失败）
                var sectionRange = doc.Range(rangeStart, rangeEnd);
                if (_anchors.TryPlace(doc, sectionRange, anchorLabel) == null)
                {
                    sectionNode.AnchorLabel = null;
                    Debug.WriteLine($"[GraphBuilder] 节点 {nodeId} 无法创建 CC 锚点，使用位置回退");
                }

                // 读取内容预览（标题后的第一行非空文本）
                sectionNode.Preview = ExtractPreview(sectionRange, h.Title);

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

        /// <summary>提取内容预览（跳过标题文本本身）</summary>
        private static string ExtractPreview(Range range, string headingTitle)
        {
            string text = range.Text ?? "";
            // 去掉标题文本本身
            int titleEnd = text.IndexOf('\r');
            if (titleEnd >= 0 && titleEnd + 1 < text.Length)
                text = text.Substring(titleEnd + 1);
            else
                return null;

            // 取第一行非空文本
            text = text.TrimStart('\r', '\n', '\a');
            if (string.IsNullOrWhiteSpace(text)) return null;

            // 截断到第一个换行或 100 字符
            int lineEnd = text.IndexOf('\r');
            if (lineEnd >= 0) text = text.Substring(0, lineEnd);
            if (text.Length > 100) text = text.Substring(0, 100) + "…";

            return text.Trim();
        }

        // ═══════════════════════════════════════════════════
        //  L2 内容元素发现
        // ═══════════════════════════════════════════════════

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
