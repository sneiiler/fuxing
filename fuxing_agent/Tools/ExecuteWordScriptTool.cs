using FuXingAgent.Core;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class ExecuteWordScriptTool
    {
        private readonly Connect _connect;
        public ExecuteWordScriptTool(Connect connect) => _connect = connect;

        /// <summary>高危关键词：匹配到则需要审核</summary>
        private static readonly Regex _dangerousPattern = new Regex(
            @"\b(Delete|Remove|Clear|SaveAs|Close|Quit|Kill)\b|\.Text\s*=\s*""""",
            RegexOptions.Compiled);
        /// <summary>匹配循环结构的开头（for/foreach/while/do），用于注入迭代守卫</summary>
        private static readonly Regex _loopPattern = new Regex(
            @"\b(?:for(?:each)?|while)\s*\("+
            @"(?:[^()]*|\((?:[^()]*|\((?:[^()]*|\([^()]*\))*\))*\))*"+
            @"\)\s*\{"+
            @"|\bdo\s*\{",
            RegexOptions.Compiled | RegexOptions.Singleline);
        [Description("Execute C# code via Microsoft.Office.Interop.Word. " +
            "Variables: app(Application), doc(ActiveDocument), sel(Selection). " +
            "Helpers: RGB(r,g,b)->WdColor, Cm()/Mm()->points. Must end with return \"result\";")]
        public string execute_word_script(
            [Description("C# 代码片段。可使用 app/doc/sel 变量，必须以 return \"...\"; 结尾")] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("缺少 code 参数");

            var app = _connect.WordApplication;
            if (app == null) throw new InvalidOperationException("Word 应用程序不可用");
            if (app.Documents.Count == 0) throw new InvalidOperationException("没有打开的文档");

            int maxIterations = new ConfigLoader().LoadConfig().MaxScriptIterations;
            var compileResult = Compile(code, maxIterations);

            if (compileResult.Errors.HasErrors)
            {
                var sb = new StringBuilder("编译错误：\n");
                foreach (CompilerError err in compileResult.Errors)
                {
                    if (!err.IsWarning)
                    {
                        int userLine = Math.Max(1, err.Line - HeaderLineCount);
                        sb.AppendLine($"  第{userLine}行 [{err.ErrorNumber}] {err.ErrorText}");
                    }
                }
                throw new InvalidOperationException(sb.ToString());
            }

            try
            {
                var scriptType = compileResult.CompiledAssembly.GetType("WordScript");
                var runMethod = scriptType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                var result = runMethod.Invoke(null, new object[] { app, app.ActiveDocument, app.Selection });
                return result?.ToString() ?? "操作完成（无返回值）";
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                throw new InvalidOperationException($"运行时错误:[{inner.GetType().Name}]: {inner.Message}");
            }
        }

        /// <summary>根据脚本内容动态判断是否包含高危操作</summary>
        public bool IsDangerous(string code) => !string.IsNullOrWhiteSpace(code) && _dangerousPattern.IsMatch(code);

        private const int HeaderLineCount = 37;

        private CompilerResults Compile(string userCode, int maxIterations)
        {
            string source = WrapCode(userCode, maxIterations);

            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false
                };

                parameters.ReferencedAssemblies.Add("System.dll");
                parameters.ReferencedAssemblies.Add("System.Core.dll");
                parameters.ReferencedAssemblies.Add("Microsoft.CSharp.dll");

                // 添加 Word Interop 程序集引用
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic) continue;
                        string name = asm.GetName().Name;
                        if (name.Contains("Microsoft.Office.Interop.Word") ||
                            name.Contains("office") || name == "stdole")
                            parameters.ReferencedAssemblies.Add(asm.Location);
                    }
                    catch { }
                }

                return provider.CompileAssemblyFromSource(parameters, source);
            }
        }

        private static string InjectLoopGuards(string code)
        {
            return _loopPattern.Replace(code, m => m.Value + " __LoopGuard.Check();");
        }

        private static string WrapCode(string userCode, int maxIterations)
        {
            string guardedCode = InjectLoopGuards(userCode);
            // 若修改此模板后必须同步更新 HeaderLineCount 常量
            return @"using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Office.Interop.Word;

public static class __LoopGuard
{
    private static int _count;
    private static int _limit;
    public static void Init(int limit) { _count = 0; _limit = limit; }
    public static void Check()
    {
        if (++_count > _limit)
            throw new InvalidOperationException(""循环次数超过限制 ("" + _limit + "")，已终止执行"");
    }
}

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
        __LoopGuard.Init(" + maxIterations + @");
        " + guardedCode + @"
    }
}";
        }
    }
}