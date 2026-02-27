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
        public override bool RequiresApproval => true;

        public override string Description =>
            "Execute C# code to manipulate the Word document (via NetOffice.WordApi).\n\n" +
            "- Pre-defined variables:\n" +
            "  app — Application object\n" +
            "  doc — ActiveDocument\n" +
            "  sel — current Selection\n\n" +
            "- Helper methods:\n" +
            "  WdColor RGB(int r, int g, int b) — RGB to Word color\n" +
            "  float Cm(float cm) — centimeters to points\n" +
            "  float Mm(float mm) — millimeters to points\n\n" +
            "- Code must end with return \"description\"; to return a result string.\n\n" +
            "- Available enums (no prefix needed): WdParagraphAlignment, WdBuiltinStyle, WdLineSpacing,\n" +
            "  WdUnderline, WdColor, WdLineStyle, WdReplace, WdUnits, WdPaperSize,\n" +
            "  WdOrientation, WdHeaderFooterIndex, WdCaptionPosition, WdBorderType, etc.\n\n" +
            "- Common API reference:\n" +
            "[Font] range.Font.Name/Size/Bold(0|1)/Italic(0|1)/Color=RGB(r,g,b)\n" +
            "[Paragraph] ParagraphFormat.Alignment / SpaceBefore / SpaceAfter / FirstLineIndent=Cm(0.74f)\n" +
            "[Line spacing] LineSpacingRule=WdLineSpacing.wdLineSpaceMultiple; LineSpacing=18f(1.5x)\n" +
            "[Style] range.set_Style(WdBuiltinStyle.wdStyleHeading1) or doc.Styles[\"name\"]\n" +
            "[Iterate paragraphs] foreach(Paragraph p in doc.Paragraphs){...} / p.OutlineLevel / p.Range\n" +
            "[Table] doc.Tables.Add(range,rows,cols) / table.Cell(r,c).Range.Text / Borders\n" +
            "[Image] doc.InlineShapes.AddPicture(path).Width/Height\n" +
            "[Find/Replace] var f=doc.Content.Find; f.Text=\"old\"; f.Replacement.Text=\"new\"; f.Execute()\n" +
            "[Caption] range.InsertCaption(\"图\", \" title\")\n" +
            "[Page] doc.PageSetup.TopMargin=Cm(2.54f) / PaperSize / Orientation\n" +
            "[Header/Footer] doc.Sections[1].Headers[WdHeaderFooterIndex.wdHeaderFooterPrimary].Range\n" +
            "[TOC] doc.TablesOfContents.Add(range)\n" +
            "[Save] doc.Save() / doc.SaveAs(path)\n\n" +
            "- Note: collection indices start at 1 (Paragraphs[1], Tables[1], Sections[1]).\n" +
            "- Compilation errors return line numbers and descriptions — fix and retry accordingly.";

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
        private const int HeaderLineCount = 25;

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
            // 当前用户代码起始行 = 第 26 行（HeaderLineCount + 1）
            return @"using System;
using System.Linq;
using System.Collections.Generic;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

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

    public static string Run(Application app, Document doc, Selection sel)
    {
        " + userCode + @"
    }
}";
        }
    }
}
