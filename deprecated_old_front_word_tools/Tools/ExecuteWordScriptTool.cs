using Microsoft.CSharp;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 将 Word COM API 直接暴露给 LLM —— 通过运行时编译执行 C# 代码片段。
    /// LLM 可使用完整的 NetOffice.WordApi 进行任意 Word 操作，
    /// 编译错误会以结构化格式返回，LLM 可据此修正代码后重试。
    /// </summary>
    public class ExecuteWordScriptTool : ToolBase
    {
        public override string Name => "execute_word_script";
        public override string DisplayName => "执行Word脚本";
        public override ToolCategory Category => ToolCategory.Advanced;
        public override bool RequiresApproval => true;  // 静态标记为 true，动态判断在下方

        // 高危关键词：匹配到则需要审批
        private static readonly System.Text.RegularExpressions.Regex _dangerousPattern =
            new System.Text.RegularExpressions.Regex(
                @"\b(Delete|Remove|Clear|SaveAs|Close|Quit|Kill)\b|\.Text\s*=\s*""""",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// 根据脚本代码内容动态判断是否需要审批：
        /// 仅当代码包含删除、清空、另存等高危操作时才需要审批，
        /// 查找、选中、读取等只读操作不需要审批。
        /// </summary>
        public override bool ShouldRequireApproval(JObject arguments)
        {
            string code = arguments?["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code)) return false;
            return _dangerousPattern.IsMatch(code);
        }

        public override string Description =>
            "Execute C# code via NetOffice.WordApi. Variables: app(Application), doc(ActiveDocument), sel(Selection). " +
            "Helpers: RGB(r,g,b)→WdColor, Cm()/Mm()→points, ResizeShape(InlineShape,widthCm)→proportional resize. Must end with return \"result\";\n" +
            "API: range.Font.Name/Size/Bold/Color=RGB() | ParagraphFormat.Alignment/SpaceBefore/SpaceAfter/FirstLineIndent=Cm() " +
            "| LineSpacingRule=WdLineSpacing.wdLineSpaceMultiple;LineSpacing=18f " +
            "| range.set_Style(WdBuiltinStyle.wdStyleHeading1) | doc.Styles[\"name\"] " +
            "| foreach(Paragraph p in doc.Paragraphs) | doc.Tables.Add(range,rows,cols) | table.Cell(r,c).Range " +
            "| doc.InlineShapes.AddPicture(path) | Find: doc.Content.Find | range.InsertCaption(\"图\",\" title\") " +
            "| doc.PageSetup | doc.Sections[1].Headers[WdHeaderFooterIndex.wdHeaderFooterPrimary].Range " +
            "| doc.TablesOfContents.Add(range) | doc.Save()/SaveAs(path)\n" +
            "Indices start at 1. Enums: WdParagraphAlignment, WdBuiltinStyle, WdLineSpacing, WdColor, WdLineStyle, MsoTriState (use short name directly). etc.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["code"] = new JObject
                {
                    ["type"] = "string",
                    ["description"] = "C# 代码片段。可使用 app/doc/sel 变量，必须以 return \"...\"; 结尾。"
                }
            },
            ["required"] = new JArray("code")
        };

        /// <summary>包装模板中用户代码前的行数，用于将编译错误行号映射回用户代码行号</summary>
        private const int HeaderLineCount = 36;

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            string code = arguments?["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code))
                return Task.FromResult(ToolExecutionResult.Fail("缺少 code 参数"));

            var app = connect.WordApplication;
            if (app == null)
                return Task.FromResult(ToolExecutionResult.Fail("Word 应用程序不可用"));

            if (app.Documents.Count == 0)
                return Task.FromResult(ToolExecutionResult.Fail("没有打开的文档"));

            // ── 编译 ──
            CompilerResults compileResult;
            try
            {
                compileResult = Compile(code);
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolExecutionResult.Fail($"编译器初始化失败: {ex.Message}"));
            }

            if (compileResult.Errors.HasErrors)
            {
                var sb = new StringBuilder("编译错误：\n");
                foreach (CompilerError err in compileResult.Errors)
                {
                    if (!err.IsWarning)
                    {
                        int userLine = Math.Max(1, err.Line - HeaderLineCount);
                        sb.AppendLine($"  第{userLine}行: [{err.ErrorNumber}] {err.ErrorText}");
                    }
                }
                return Task.FromResult(ToolExecutionResult.Fail(sb.ToString()));
            }

            // ── 执行 ──
            try
            {
                var scriptType = compileResult.CompiledAssembly.GetType("WordScript");
                var runMethod = scriptType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                var result = runMethod.Invoke(null, new object[] { app, app.ActiveDocument, app.Selection });
                return Task.FromResult(ToolExecutionResult.Ok(result?.ToString() ?? "操作完成（无返回值）"));
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                return Task.FromResult(ToolExecutionResult.Fail(
                    $"运行时错误 [{inner.GetType().Name}]: {inner.Message}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolExecutionResult.Fail(
                    $"执行失败 [{ex.GetType().Name}]: {ex.Message}"));
            }
        }

        private CompilerResults Compile(string userCode)
        {
            string source = WrapCode(userCode);

            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false
                };

                // 基础程序集
                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add("Microsoft.CSharp.dll");

                // 从当前 AppDomain 查找 NetOffice 相关程序集
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic) continue;
                        string name = asm.GetName().Name;
                        if (name == "NetOffice" || name == "WordApi" || name == "OfficeApi" || name == "stdole")
                            parameters.ReferencedAssemblies.Add(asm.Location);
                    }
                    catch
                    {
                        // 忽略无法访问 Location 的动态程序集
                    }
                }

                return provider.CompileAssemblyFromSource(parameters, source);
            }
        }

        private static string WrapCode(string userCode)
        {
            // ⚠ 修改此模板后必须同步更新 HeaderLineCount 常量
            // 当前用户代码起始行 = 第 36 行（HeaderLineCount + 1）
            return @"using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;
using NetOffice.OfficeApi.Enums;

public static class WordScript
{
    public static WdColor RGB(int r, int g, int b)
    {
        return (WdColor)(r | (g << 8) | (b << 16));
    }

    public static float Cm(float cm)
    {
        return cm * 28.3465f;
    }

    public static float Mm(float mm)
    {
        return mm * 2.83465f;
    }

    /// <summary>等比缩放 InlineShape 到指定宽度(cm)，自动保持纵横比</summary>
    public static void ResizeShape(InlineShape shape, float widthCm)
    {
        float ratio = shape.Height / shape.Width;
        shape.Width = Cm(widthCm);
        shape.Height = Cm(widthCm) * ratio;
    }

    public static string Run(Application app, Document doc, Selection sel)
    {
        " + userCode + @"
    }
}";  
        }
    }
}
