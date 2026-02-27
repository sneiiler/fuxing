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
    //  文档 AST 构建器
    //
    //  两条路径：
    //  1. 快速路径 — 文档使用了标准 Heading 样式 → 直接从 OutlineLevel 构建树
    //  2. LLM 路径 — 文档未使用标题样式 → 发送格式特征给 LLM，
    //     LLM 定义"格式签名→层级"映射规则，程序化应用到全部段落
    //
    //  关键：LLM 只做一次判断（定义映射规则），
    //  不逐段分析——用确定性逻辑保证一致性。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 文档 AST 构建器。根据段落元数据构建树状文档结构。
    /// </summary>
    public class DocumentAstBuilder
    {
        /// <summary>至少需要这么多个 OutlineLevel 标题才走快速路径</summary>
        private const int MinHeadingCountThreshold = 2;

        /// <summary>发送给 LLM 的格式采样段落数上限</summary>
        private const int MaxSampleParagraphs = 50;

        /// <summary>
        /// 从段落元数据构建文档 AST。
        /// 如果文档标题样式规范，直接用 OutlineLevel 构建；
        /// 否则调用 LLM 推断格式→层级映射后程序化构建。
        /// </summary>
        public async Task<DocumentMap> BuildAsync(
            DocumentStructure rawStructure,
            CancellationToken cancellation = default)
        {
            int headingCount = rawStructure.Paragraphs
                .Count(p => p.OutlineLevel >= 1 && p.OutlineLevel <= 6 && !p.IsBlank);

            List<InferredHeading> headings;
            bool llmAssisted;

            // 非空段落数太少时，文档内容不足以区分标题与正文，直接返回平面树
            int nonBlankCount = rawStructure.Paragraphs.Count(p => !p.IsBlank);

            if (headingCount >= MinHeadingCountThreshold)
            {
                // ═══ 快速路径：文档已有规范标题 ═══
                Debug.WriteLine($"[AstBuilder] 检测到 {headingCount} 个规范标题，直接构建 AST");
                headings = ExtractFromOutlineLevel(rawStructure);
                llmAssisted = false;
            }
            else if (nonBlankCount <= 3)
            {
                // ═══ 简单文档路径：内容太少，无法推断标题规则，直接返回平面树 ═══
                Debug.WriteLine($"[AstBuilder] 仅 {nonBlankCount} 个非空段落，跳过 LLM 推断，返回平面树");
                headings = new List<InferredHeading>();
                llmAssisted = false;
            }
            else
            {
                // ═══ LLM 路径：请 LLM 根据格式特征推断层级 ═══
                Debug.WriteLine($"[AstBuilder] 仅 {headingCount} 个规范标题，启动 LLM 推断");
                headings = await InferHeadingsWithLlm(rawStructure, cancellation);
                llmAssisted = true;
            }

            // 构建树
            var root = BuildTree(rawStructure, headings);

            // 构建索引
            var index = new Dictionary<string, DocumentAstNode>();
            BuildIndex(root, index);

            // 计算段落统计
            ComputeParaCounts(root, rawStructure.TotalParagraphs);

            return new DocumentMap
            {
                DocumentName = rawStructure.DocumentName,
                ContentHash = 0, // 由调用方（DocumentMapCache）设置
                Root = root,
                Index = index,
                RawStructure = rawStructure,
                LlmAssisted = llmAssisted,
                BuiltAt = DateTime.Now
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  快速路径：从 OutlineLevel 提取标题
        // ═══════════════════════════════════════════════════════════════

        private static List<InferredHeading> ExtractFromOutlineLevel(DocumentStructure structure)
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
        //  LLM 路径：推断格式→层级映射
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 调用 LLM 一次：发送格式特征采样 → LLM 返回映射规则 JSON → 程序化应用。
        /// LLM 只做一次判断，剩下的全是确定性逻辑。
        /// </summary>
        private async Task<List<InferredHeading>> InferHeadingsWithLlm(
            DocumentStructure structure,
            CancellationToken cancellation)
        {
            string featureSummary = BuildFormatFeatureSummary(structure);

            var request = new SubAgentRequest
            {
                Task = SubAgentTask.Custom,
                Prompt = BuildLevelInferencePrompt(featureSummary),
                MaxRounds = 1, // 一轮推理，不需要工具
                ToolDefinitions = null,
                ToolExecutor = null
            };

            var agent = new SubAgent();
            var result = await agent.RunAsync(request, cancellation);

            if (!result.Success)
                throw new InvalidOperationException($"LLM 层级推断失败: {result.Output}");

            var rules = ParseLevelMappingRules(result.Output);

            if (rules.Count == 0)
            {
                // LLM 认为文档没有可识别的标题模式，优雅降级为平面树
                Debug.WriteLine("[AstBuilder] LLM 未返回映射规则，文档可能没有明显的标题结构，返回空列表");
                return new List<InferredHeading>();
            }

            Debug.WriteLine($"[AstBuilder] LLM 推断出 {rules.Count} 条映射规则:");
            foreach (var r in rules)
                Debug.WriteLine($"  Level {r.Level}: {r.Description}");

            return ApplyRules(structure, rules);
        }

        /// <summary>构建格式特征摘要（只发送必要信息，不发全文）</summary>
        private static string BuildFormatFeatureSummary(DocumentStructure structure)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 文档格式概况 ===");
            sb.AppendLine($"文档名: {structure.DocumentName}");
            sb.AppendLine($"总段落数: {structure.TotalParagraphs}");
            sb.AppendLine($"出现的字号: {string.Join(", ", structure.DistinctFontSizes.Select(s => s + "pt"))}");
            sb.AppendLine($"出现的样式: {string.Join(", ", structure.DistinctStyles)}");
            sb.AppendLine();
            sb.AppendLine($"=== 段落格式采样（前 {MaxSampleParagraphs} 个非空段落） ===");

            int count = 0;
            foreach (var p in structure.Paragraphs)
            {
                if (p.IsBlank) continue;
                sb.AppendLine(p.ToDescriptionLine());
                count++;
                if (count >= MaxSampleParagraphs) break;
            }

            return sb.ToString();
        }

        /// <summary>构建 LLM 层级推断的 Prompt</summary>
        private static string BuildLevelInferencePrompt(string featureSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个文档格式分析专家。");
            sb.AppendLine("下面提供了一份 Word 文档的段落格式信息采样。");
            sb.AppendLine("这份文档没有正确使用 Word 的标题样式（Heading 1~6），");
            sb.AppendLine("但作者通过字号、加粗、居中等排版特征来区分标题与正文。");
            sb.AppendLine();
            sb.AppendLine("请分析这些格式特征，输出一组「格式签名→标题层级」的映射规则。");
            sb.AppendLine();
            sb.AppendLine("规则说明：");
            sb.AppendLine("- 每条规则描述一种标题的格式特征组合及其对应的层级（1-6）");
            sb.AppendLine("- 字号越大、加粗、居中 通常意味着越高层级");
            sb.AppendLine("- 不是所有段落都是标题，正文段落不需要映射");
            sb.AppendLine("- 只输出你有信心判断为标题的规则");
            sb.AppendLine();
            sb.AppendLine("请严格按以下 JSON 格式输出（不要输出任何其他内容）：");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"level\": 1,");
            sb.AppendLine("    \"min_font_size\": 18.0,");
            sb.AppendLine("    \"max_font_size\": 22.0,");
            sb.AppendLine("    \"bold\": true,");
            sb.AppendLine("    \"alignment\": \"居中\",");
            sb.AppendLine("    \"description\": \"大号加粗居中，一级标题\"");
            sb.AppendLine("  },");
            sb.AppendLine("  {");
            sb.AppendLine("    \"level\": 2,");
            sb.AppendLine("    \"min_font_size\": 14.0,");
            sb.AppendLine("    \"max_font_size\": 16.0,");
            sb.AppendLine("    \"bold\": true,");
            sb.AppendLine("    \"alignment\": null,");
            sb.AppendLine("    \"description\": \"中等加粗，二级标题\"");
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("字段说明：");
            sb.AppendLine("- level: 1-6，标题层级");
            sb.AppendLine("- min_font_size / max_font_size: 字号范围（pt），null 表示不限制字号");
            sb.AppendLine("- bold: true/false/null（null 表示不限制加粗）");
            sb.AppendLine("- alignment: '居中'/'左对齐'/'右对齐'/'两端对齐'/null（null 表示不限制对齐）");
            sb.AppendLine("- description: 规则说明（给人阅读的备注）");
            sb.AppendLine();
            sb.AppendLine("以下是文档格式数据：");
            sb.AppendLine(featureSummary);

            return sb.ToString();
        }

        /// <summary>从 LLM 输出中解析映射规则 JSON</summary>
        private static List<LevelMappingRule> ParseLevelMappingRules(string llmOutput)
        {
            string json = ExtractJsonArray(llmOutput);
            if (string.IsNullOrEmpty(json))
                return new List<LevelMappingRule>();

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
            var rules = new List<LevelMappingRule>();

            foreach (var item in arr)
            {
                var rule = new LevelMappingRule
                {
                    Level = item["level"]?.Value<int>() ?? 0,
                    MinFontSize = item["min_font_size"]?.Type == JTokenType.Null
                        ? null : item["min_font_size"]?.Value<float?>(),
                    MaxFontSize = item["max_font_size"]?.Type == JTokenType.Null
                        ? null : item["max_font_size"]?.Value<float?>(),
                    Bold = item["bold"]?.Type == JTokenType.Null
                        ? null : item["bold"]?.Value<bool?>(),
                    Alignment = item["alignment"]?.Type == JTokenType.Null
                        ? null : item["alignment"]?.Value<string>(),
                    Description = item["description"]?.Value<string>()
                };

                if (rule.Level >= 1 && rule.Level <= 6)
                    rules.Add(rule);
            }

            return rules;
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

        /// <summary>用映射规则程序化地标记标题段落</summary>
        private static List<InferredHeading> ApplyRules(
            DocumentStructure structure, List<LevelMappingRule> rules)
        {
            var headings = new List<InferredHeading>();

            // 按层级排序（高层级优先匹配，避免低层级规则误捕获高层级标题）
            var sortedRules = rules.OrderBy(r => r.Level).ToList();

            foreach (var p in structure.Paragraphs)
            {
                if (p.IsBlank || p.IsInTable) continue;

                foreach (var rule in sortedRules)
                {
                    if (rule.Matches(p))
                    {
                        headings.Add(new InferredHeading
                        {
                            ParaIndex = p.Index,
                            Level = rule.Level,
                            Title = p.Text
                        });
                        break; // 一个段落只匹配一条规则
                    }
                }
            }

            Debug.WriteLine($"[AstBuilder] 映射规则匹配了 {headings.Count} 个标题段落");
            return headings;
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
        //  索引与统计
        // ═══════════════════════════════════════════════════════════════

        private static void BuildIndex(DocumentAstNode node, Dictionary<string, DocumentAstNode> index)
        {
            if (node.Type != AstNodeType.Root)
                index[node.NodeId] = node;

            foreach (var child in node.Children)
                BuildIndex(child, index);
        }

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

        /// <summary>
        /// LLM 推断的格式→层级映射规则。
        /// 描述一种标题的格式特征组合（字号范围、加粗、对齐）及其对应层级。
        /// </summary>
        private class LevelMappingRule
        {
            public int Level { get; set; }
            public float? MinFontSize { get; set; }
            public float? MaxFontSize { get; set; }
            public bool? Bold { get; set; }
            public string Alignment { get; set; }
            public string Description { get; set; }

            /// <summary>检查段落是否匹配此规则</summary>
            public bool Matches(ParagraphMeta para)
            {
                // 如果规则指定了字号范围，段落必须有有效字号
                if ((MinFontSize.HasValue || MaxFontSize.HasValue) && para.FontSize < 0)
                    return false;

                if (MinFontSize.HasValue && para.FontSize < MinFontSize.Value)
                    return false;

                if (MaxFontSize.HasValue && para.FontSize > MaxFontSize.Value)
                    return false;

                // 加粗检查
                if (Bold.HasValue && para.Bold != Bold.Value)
                    return false;

                // 对齐检查
                if (!string.IsNullOrEmpty(Alignment) && para.Alignment != Alignment)
                    return false;

                // 过短的段落不太可能是标题
                if (para.Text == null || para.Text.Length < 2)
                    return false;

                return true;
            }
        }
    }
}
