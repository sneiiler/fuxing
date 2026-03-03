using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FuXing.SubAgents
{
    // ═══════════════════════════════════════════════════════════════
    //  文档 AST 构建器（深度路径专用）
    //
    //  仅被 DocumentGraphBuilder.BuildSkeletonDeepAsync 调用。
    //  负责：证据收集（OutlineLevel + 单行候选）→ LLM 裁定标题层级。
    //  返回 DocumentAstNode 根节点，由 GraphBuilder 转化为 DocNode 图。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档 AST 构建器（深度路径专用）。
    /// 从段落元数据 + LLM 推断构建标题树，返回 DocumentAstNode 根节点。
    /// </summary>
    public class DocumentAstBuilder
    {
        /// <summary>发送给 LLM 的格式采样段落数上限</summary>
        private const int MaxSampleParagraphs = 50;

        /// <summary>单行候选段落的最大字符数阈值</summary>
        private const int SingleLineCandidateMaxLength = 80;

        // ═══════════════════════════════════════════════════════════════
        //  深度路径：证据收集 + LLM 裁定（含格式分析）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 深度路径：从段落元数据构建文档 AST 根节点。
        /// 收集 OutlineLevel 提示和单行候选段落，交给 LLM 统一裁定标题层级。
        /// 返回 DocumentAstNode 根节点（不再包装为 DocumentMap）。
        /// </summary>
        public async Task<DocumentAstNode> BuildAsync(
            DocumentStructure rawStructure,
            CancellationToken cancellation = default)
        {
            List<InferredHeading> headings;

            // 非空段落数太少时，文档内容不足以区分标题与正文，直接返回平面树
            int nonBlankCount = rawStructure.Paragraphs.Count(p => !p.IsBlank);

            if (nonBlankCount <= 3)
            {
                Debug.WriteLine($"[AstBuilder] 仅 {nonBlankCount} 个非空段落，跳过 LLM 推断，返回平面树");
                headings = new List<InferredHeading>();
            }
            else
            {
                // ═══ 深度路径：证据收集 → LLM 裁定 ═══

                // Phase 1: 收集 OutlineLevel 提示（Word 样式中已标记的标题）
                var outlineLevelHints = ExtractOutlineLevelHints(rawStructure);
                Debug.WriteLine($"[AstBuilder] 检测到 {outlineLevelHints.Count} 个 OutlineLevel 提示");

                // Phase 2: 收集单行候选段落（可能是标题但未被样式标记）
                var hintedIndices = new HashSet<int>(outlineLevelHints.Select(h => h.ParaIndex));
                var singleLineCandidates = FindSingleLineCandidates(rawStructure, hintedIndices);
                Debug.WriteLine($"[AstBuilder] 发现 {singleLineCandidates.Count} 个单行候选段落");

                // Phase 3: LLM 统一裁定
                headings = await InferHeadingsWithLlm(
                    rawStructure, outlineLevelHints, singleLineCandidates, cancellation);
            }

            // 构建树
            var root = BuildTree(rawStructure, headings);

            // 计算段落统计
            ComputeParaCounts(root, rawStructure.TotalParagraphs);

            return root;
        }

        // ═══════════════════════════════════════════════════════════════
        //  证据收集
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从 OutlineLevel 提取标题提示（作为 LLM 的参考证据）</summary>
        private static List<InferredHeading> ExtractOutlineLevelHints(DocumentStructure structure)
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

        // ═══════════════════════════════════════════════════════════════
        //  单行候选检测
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 找出可能是标题的单行候选段落。
        /// 条件：非空白、不在表格内、非列表项、文本长度 ≤ 阈值、
        /// 尚未被 OutlineLevel 标记。
        /// </summary>
        private static List<int> FindSingleLineCandidates(
            DocumentStructure structure, HashSet<int> hintedParaIndices)
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

        /// <summary>
        /// 为单行候选段落构建上下文描述（前后各 1 个非空段落）。
        /// </summary>
        private static string BuildCandidateContext(
            DocumentStructure structure, int paraIndex)
        {
            var sb = new StringBuilder();
            var para = structure.Paragraphs[paraIndex - 1]; // 1-based → 0-based

            // 向前找 1 个非空段落
            ParagraphMeta prevPara = null;
            for (int i = paraIndex - 2; i >= 0; i--)
            {
                if (!structure.Paragraphs[i].IsBlank)
                {
                    prevPara = structure.Paragraphs[i];
                    break;
                }
            }

            // 向后找 1 个非空段落
            ParagraphMeta nextPara = null;
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

        // ═══════════════════════════════════════════════════════════════
        //  LLM 统一裁定
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 将所有证据（OutlineLevel 提示 + 单行候选 + 格式采样）交给 LLM，
        /// LLM 一次性返回最终标题列表。
        /// </summary>
        private async Task<List<InferredHeading>> InferHeadingsWithLlm(
            DocumentStructure structure,
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
                Debug.WriteLine("[AstBuilder] LLM 未返回任何标题，文档可能没有明显的标题结构");
            else
                Debug.WriteLine($"[AstBuilder] LLM 确定了 {headings.Count} 个标题");

            return headings;
        }

        /// <summary>构建格式特征摘要</summary>
        private static string BuildFormatFeatureSummary(DocumentStructure structure)
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
            DocumentStructure structure,
            List<InferredHeading> outlineLevelHints,
            List<int> singleLineCandidates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个文档结构分析专家。请分析以下 Word 文档的段落信息，确定哪些段落是标题以及它们的层级。");
            sb.AppendLine();

            // 材料 1: 格式概况
            sb.AppendLine("=== 1. 文档格式概况 ===");
            sb.Append(BuildFormatFeatureSummary(structure));
            sb.AppendLine();

            // 材料 2: OutlineLevel 提示
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

            // 材料 3: 单行候选
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

            // 材料 4: 段落格式采样
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

            // 判断要求
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
                Debug.WriteLine($"[AstBuilder] LLM 输出的 JSON 解析失败: {ex.Message}");
                Debug.WriteLine($"[AstBuilder] 提取到的疑似 JSON: {json.Substring(0, Math.Min(200, json.Length))}");
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
            // 先尝试找 ```json ... ``` 代码块中的数组
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

            // 直接找 [ ... ]，但必须确认是合法的 JSON 数组开头
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

        /// <summary>快速检查字符串是否像一个 JSON 数组（[ 后跟 { 或 另一个 [)）</summary>
        private static bool LooksLikeJsonArray(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '[') return false;
            // 跳过开头的空白，看第一个非空白字符是否是 JSON 值的合法开头
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) continue;
                // JSON 数组元素必须以 { [ " 数字 true false null 开头
                return c == '{' || c == '[' || c == '"' || c == '-'
                    || char.IsDigit(c) || c == 't' || c == 'f' || c == 'n' || c == ']';
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  树构建
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从已标记的标题列表构建 AST 树</summary>
        private static DocumentAstNode BuildTree(
            DocumentStructure structure, List<InferredHeading> headings)
        {
            var root = new DocumentAstNode
            {
                NodeId = "root",
                Type = AstNodeType.Root,
                Level = 0,
                Title = structure.DocumentName,
                ParaStart = 1,
                ParaEnd = structure.TotalParagraphs + 1,
                CharStart = structure.Paragraphs.Count > 0
                    ? structure.Paragraphs[0].StartPosition : 0,
                CharEnd = -1 // 根节点到文档末尾
            };

            if (headings.Count == 0)
                return root;

            // 用栈构建嵌套结构
            var stack = new Stack<DocumentAstNode>();
            stack.Push(root);

            for (int i = 0; i < headings.Count; i++)
            {
                var h = headings[i];
                var para = structure.Paragraphs[h.ParaIndex - 1]; // 1-based → 0-based

                // 下一个标题的段落索引（当前章节的段落范围终止于此）
                int nextParaIndex = (i + 1 < headings.Count)
                    ? headings[i + 1].ParaIndex
                    : structure.TotalParagraphs + 1;

                // CharEnd: 下一个标题段落的起始位置，或 -1 表示到文档末尾
                int charEnd;
                if (nextParaIndex <= structure.TotalParagraphs)
                    charEnd = structure.Paragraphs[nextParaIndex - 1].StartPosition;
                else
                    charEnd = -1;

                var node = new DocumentAstNode
                {
                    NodeId = DocumentAstNode.ComputeNodeId(h.Level, h.Title, h.ParaIndex),
                    Type = AstNodeType.Section,
                    Level = h.Level,
                    Title = h.Title,
                    ParaStart = h.ParaIndex,
                    ParaEnd = nextParaIndex,
                    CharStart = para.StartPosition,
                    CharEnd = charEnd,
                    ContentPreview = BuildContentPreview(structure, h.ParaIndex, nextParaIndex)
                };

                // 退栈到合适的父节点：level < 当前节点 level 的最近祖先
                while (stack.Count > 1 && stack.Peek().Level >= h.Level)
                    stack.Pop();

                stack.Peek().Children.Add(node);
                stack.Push(node);
            }

            return root;
        }

        /// <summary>生成节点内容预览（取标题后第一个非空段落的前 80 字符）</summary>
        private static string BuildContentPreview(
            DocumentStructure structure, int paraStart, int paraEnd)
        {
            // 跳过标题段本身（paraStart），找后面第一个非空段落
            for (int idx = paraStart + 1; idx < paraEnd && idx <= structure.TotalParagraphs; idx++)
            {
                var p = structure.Paragraphs[idx - 1]; // 1-based → 0-based
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

        // ═══════════════════════════════════════════════════════════════
        //  统计
        // ═══════════════════════════════════════════════════════════════

        /// <summary>递归计算每个节点的段落统计</summary>
        private static void ComputeParaCounts(DocumentAstNode node, int totalParagraphs)
        {
            if (node.Type == AstNodeType.Root)
            {
                node.TotalParaCount = totalParagraphs;
                node.DirectParaCount = 0;
                foreach (var child in node.Children)
                    ComputeParaCounts(child, totalParagraphs);
                return;
            }

            int totalChildParas = 0;
            foreach (var child in node.Children)
            {
                ComputeParaCounts(child, totalParagraphs);
                totalChildParas += child.TotalParaCount;
            }

            node.TotalParaCount = node.ParaEnd - node.ParaStart;
            node.DirectParaCount = node.TotalParaCount - totalChildParas;
        }

        // ═══════════════════════════════════════════════════════════════
        //  内部数据结构
        // ═══════════════════════════════════════════════════════════════

        /// <summary>推断出的标题信息</summary>
        private class InferredHeading
        {
            public int ParaIndex { get; set; }  // 1-based 段落索引
            public int Level { get; set; }       // 1-6 标题层级
            public string Title { get; set; }    // 标题文本
        }
    }
}
