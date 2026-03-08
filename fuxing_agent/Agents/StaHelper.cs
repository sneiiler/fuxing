using System;

namespace FuXingAgent.Agents
{
    /// <summary>
    /// 将委托 marshal 到 Word STA 主线程执行。
    /// Workflow 在后台线程运行时，通过此类将 COM 操作送回主线程。
    /// </summary>
    internal static class StaHelper
    {
        public static T RunOnSta<T>(Func<T> func)
        {
            var invoker = ToolInvocationScope.CurrentOptions.Value?.InvokeOnSta;
            if (invoker == null) return func();
            return (T)invoker(() => func());
        }

        public static void RunOnSta(Action action)
        {
            var invoker = ToolInvocationScope.CurrentOptions.Value?.InvokeOnSta;
            if (invoker == null) { action(); return; }
            invoker(() => { action(); return null; });
        }
    }
}
