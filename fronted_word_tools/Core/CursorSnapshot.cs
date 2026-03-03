namespace FuXing
{
    /// <summary>
    /// 用户发送消息时的光标/选区位置快照。
    /// 工具执行期间用户可能移动光标，所有需要"当前光标位置"的工具
    /// 应读取此快照而非实时 Selection。
    /// </summary>
    public class CursorSnapshot
    {
        /// <summary>选区起始字符偏移</summary>
        public int Start { get; }

        /// <summary>选区结束字符偏移</summary>
        public int End { get; }

        /// <summary>是否为插入点（无选中文本）</summary>
        public bool IsInsertionPoint => Start == End;

        public CursorSnapshot(int start, int end)
        {
            Start = start;
            End = end;
        }
    }
}
