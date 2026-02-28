using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace FuXing
{
    // ═══════════════════════════════════════════════════════════════
    //  自定义异常：参数校验失败
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 工具参数校验失败时抛出。
    /// ToolRegistry 会捕获此异常并直接返回 Fail，不记录冗长的堆栈信息。
    /// </summary>
    public class ToolArgumentException : Exception
    {
        public ToolArgumentException(string message) : base(message) { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  工具抽象基类
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <para>所有 LLM tool 的推荐基类，提供：</para>
    /// <list type="bullet">
    ///   <item>统一的参数提取方法（RequireString / OptionalBool 等），消除 JObject 取值方式不一致</item>
    ///   <item>文档就绪守卫（RequireActiveDocument）</item>
    ///   <item>修订追踪安全作用域（TrackRevisionsScope，基于 IDisposable + using）</item>
    ///   <item>工具分类（Category）</item>
    /// </list>
    /// </summary>
    public abstract class ToolBase : ITool
    {
        // ── ITool 抽象成员 ──

        public abstract string Name { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract JObject Parameters { get; }
        public virtual ToolCategory Category => ToolCategory.Advanced;

        /// <summary>是否需要用户审批确认。子类可覆写返回 true。</summary>
        public virtual bool RequiresApproval => false;

        /// <summary>
        /// 根据实际调用参数判断是否需要审批确认。
        /// 默认回退到 <see cref="RequiresApproval"/> 属性；子类可覆写实现动态判断。
        /// </summary>
        public virtual bool ShouldRequireApproval(JObject arguments) => RequiresApproval;

        /// <summary>子类实现具体逻辑</summary>
        public abstract Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments);

        // ═══════════════════════════════════════════════════════════════
        //  参数提取——统一风格，消除 4 种 JObject 取值写法共存的问题
        // ═══════════════════════════════════════════════════════════════

        /// <summary>提取必填 string 参数，缺失或空白时抛出 <see cref="ToolArgumentException"/></summary>
        protected static string RequireString(JObject args, string key)
        {
            string val = args?[key]?.ToString();
            if (string.IsNullOrWhiteSpace(val))
                throw new ToolArgumentException($"缺少必填参数: {key}");
            return val;
        }

        /// <summary>提取可选 string 参数</summary>
        protected static string OptionalString(JObject args, string key, string defaultValue = null)
        {
            return args?[key]?.ToString() ?? defaultValue;
        }

        /// <summary>提取必填 int 参数</summary>
        protected static int RequireInt(JObject args, string key)
        {
            var token = args?[key];
            if (token == null || token.Type == JTokenType.Null)
                throw new ToolArgumentException($"缺少必填参数: {key}");
            return token.Value<int>();
        }

        /// <summary>提取可选 int 参数</summary>
        protected static int OptionalInt(JObject args, string key, int defaultValue)
        {
            var token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            return token.Value<int>();
        }

        /// <summary>提取可选 bool 参数</summary>
        protected static bool OptionalBool(JObject args, string key, bool defaultValue)
        {
            var token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            return token.Value<bool>();
        }

        /// <summary>提取可选 double 参数</summary>
        protected static double OptionalDouble(JObject args, string key, double defaultValue)
        {
            var token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            return token.Value<double>();
        }

        /// <summary>提取可选 float 参数</summary>
        protected static float OptionalFloat(JObject args, string key, float defaultValue)
        {
            var token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return defaultValue;
            return token.Value<float>();
        }

        /// <summary>提取可选的 JArray 参数</summary>
        protected static JArray OptionalArray(JObject args, string key)
        {
            return args?[key] as JArray;
        }

        /// <summary>提取可选的嵌套 JObject 参数</summary>
        protected static JObject OptionalObject(JObject args, string key)
        {
            return args?[key] as JObject;
        }

        // ═══════════════════════════════════════════════════════════════        //  插入位置守卫
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// 确保非文本内容（图片、表格、目录等）插入在独立段落上。
        /// 如果光标所在段落已有文字内容，先插入一个新段落，避免格式污染。
        /// 如果光标已在空行上，不做任何操作。
        /// </summary>
        protected static void EnsureNewParagraphIfNeeded(NetOffice.WordApi.Application app)
        {
            var sel = app.Selection;
            var curPara = sel.Range.Paragraphs[1];
            string paraText = curPara.Range.Text?.TrimEnd('\r', '\n', '\a') ?? "";
            if (paraText.Length > 0)
                sel.TypeParagraph();
        }

        // ═════════════════════════════════════════════════════════════        //  文档就绪守卫
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 获取当前活动文档，如果没有打开的文档则抛出 <see cref="ToolArgumentException"/>。
        /// 此方法统一了 11 个工具中缺失的"打开文档"前置检查。
        /// </summary>
        protected static NetOffice.WordApi.Document RequireActiveDocument(Connect connect)
        {
            var app = connect.WordApplication;
            if (app == null)
                throw new ToolArgumentException("Word 应用程序不可用");
            if (app.Documents.Count == 0)
                throw new ToolArgumentException("没有打开的文档");
            return app.ActiveDocument;
        }

        // ═══════════════════════════════════════════════════════════════
        //  修订追踪安全作用域
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// <para>创建一个修订追踪安全作用域，用 using 语句包裹即可保证异常时自动关闭修订追踪。</para>
        /// <code>
        /// using (BeginTrackRevisions(connect))
        /// {
        ///     // 修改文档…
        /// }
        /// </code>
        /// </summary>
        protected static TrackRevisionsScope BeginTrackRevisions(Connect connect)
        {
            return new TrackRevisionsScope(connect);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  修订追踪 RAII 作用域
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// IDisposable 包装：构造时开启修订追踪，Dispose 时关闭。
    /// 替代手动 try/finally 模式，防止异常时修订追踪泄漏。
    /// </summary>
    public sealed class TrackRevisionsScope : IDisposable
    {
        private readonly Connect _connect;
        private bool _disposed;

        public TrackRevisionsScope(Connect connect)
        {
            _connect = connect;
            _connect.EnsureTrackRevisions();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connect.StopTrackRevisions();
        }
    }
}
