using System;
using System.ComponentModel;
using Word = Microsoft.Office.Interop.Word;

namespace FuXingAgent.Tools
{
    public class ToggleTrackChangesTool
    {
        private readonly Connect _connect;
        public ToggleTrackChangesTool(Connect connect) => _connect = connect;

        [Description("Toggle Track Changes (revision tracking) on/off, or check status. action: on/off/status. accept_all: when turning off, accept all pending revisions first.")]
        public string toggle_track_changes(
            [Description("操作: on/off/status")] string action = "status",
            [Description("关闭时是否先接受全部修订")] bool accept_all = false)
        {
            var app = _connect.WordApplication;
            var doc = app.ActiveDocument ?? throw new InvalidOperationException("没有活动文档");

            switch (action)
            {
                case "on":
                    doc.TrackRevisions = true;
                    return "已开启修订追踪";

                case "off":
                    if (accept_all && doc.Revisions.Count > 0)
                    {
                        int count = doc.Revisions.Count;
                        doc.Revisions.AcceptAll();
                        doc.TrackRevisions = false;
                        return $"已接受全部{count} 处修订并关闭追踪";
                    }
                    doc.TrackRevisions = false;
                    return "已关闭修订追踪";

                case "status":
                    bool isOn = doc.TrackRevisions;
                    int pending = doc.Revisions.Count;
                    return $"修订追踪: {(isOn ? "开启" : "关闭")}，待处理修订: {pending} 处";

                default:
                    throw new ArgumentException($"未知 action: {action}（可用 on/off/status）");
            }
        }
    }
}