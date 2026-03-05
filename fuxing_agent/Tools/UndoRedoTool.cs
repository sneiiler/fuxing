using System;
using System.ComponentModel;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class UndoRedoTool
    {
        private readonly Connect _connect;

        public UndoRedoTool(Connect connect) => _connect = connect;

        [Description("Undo or redo document operations. action: undo (default), redo. times: number of steps (1-50).")]
        public string undo_redo(
            [Description("操作类型: undo/redo")] string action = "undo",
            [Description("操作次数（1-50）")] int times = 1)
        {
            var app = _connect.WordApplication;
            if (app?.Documents.Count == 0)
                throw new InvalidOperationException("没有活动文档");

            if (times < 1) times = 1;
            if (times > 50) times = 50;

            int done = 0;
            for (int i = 0; i < times; i++)
            {
                bool ok = action == "redo" ? app.ActiveDocument.Redo() : app.ActiveDocument.Undo();
                if (!ok) break;
                done++;
            }

            string op = action == "redo" ? "重做" : "撤销";
            return done == times
                ? $"已{op} {done} 步"
                : $"已{op} {done}/{times} 步（后续无更多可{op}操作）";
        }
    }
}
