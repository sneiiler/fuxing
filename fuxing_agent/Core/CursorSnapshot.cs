using Microsoft.Office.Interop.Word;

namespace FuXingAgent.Core
{
    /// <summary>
    /// 用户发送消息时的光标/选区快照。
    /// 工具执行期间用户可能移动光标，所有需要"当前光标位置"的工具应读取此快照。
    /// </summary>
    public class CursorSnapshot
    {
        /// <summary>选区起始位置</summary>
        public int Start { get; set; }

        /// <summary>选区结束位置</summary>
        public int End { get; set; }

        /// <summary>是否为纯光标（无选区）</summary>
        public bool IsInsertionPoint { get; set; }

        /// <summary>选中的文本内容</summary>
        public string SelectedText { get; set; }

        /// <summary>从当前 Selection 创建快照</summary>
        public static CursorSnapshot FromSelection(Application app)
        {
            try
            {
                var sel = app.Selection;
                if (sel == null) return null;
                bool isInsertionPoint = sel.Start == sel.End;
                return new CursorSnapshot
                {
                    Start = sel.Start,
                    End = sel.End,
                    IsInsertionPoint = isInsertionPoint,
                    SelectedText = isInsertionPoint ? string.Empty : sel.Text
                };
            }
            catch { return null; }
        }
    }
}
