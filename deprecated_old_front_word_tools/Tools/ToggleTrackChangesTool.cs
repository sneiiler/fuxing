using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// 控制文档的修订追踪（Track Changes）功能。
    /// 开启后，所有编辑操作将以修订形式记录，用户可以逐一接受或拒绝。
    /// </summary>
    public class ToggleTrackChangesTool : ToolBase
    {
        public override string Name => "toggle_track_changes";
        public override string DisplayName => "修订追踪控制";
        public override ToolCategory Category => ToolCategory.Editing;

        public override string Description =>
            "Control Track Changes (revision tracking). action: on/off/status (default). " +
            "accept_all: accept all revisions when turning off.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["action"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray("on", "off", "status"),
                    ["description"] = "操作类型（默认 status）"
                },
                ["accept_all"] = new JObject
                {
                    ["type"] = "boolean",
                    ["description"] = "关闭时是否接受全部修订（仅 action=off 时有效）"
                }
            }
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var doc = RequireActiveDocument(connect);
            string action = OptionalString(arguments, "action", "status");

            switch (action)
            {
                case "on":
                    doc.TrackRevisions = true;
                    return Task.FromResult(
                        ToolExecutionResult.Ok("已开启修订追踪。后续所有编辑操作将以修订形式记录。"));

                case "off":
                {
                    bool acceptAll = OptionalBool(arguments, "accept_all", false);
                    if (acceptAll && doc.Revisions.Count > 0)
                    {
                        doc.Revisions.AcceptAll();
                    }
                    doc.TrackRevisions = false;
                    return Task.FromResult(
                        ToolExecutionResult.Ok("已关闭修订追踪。" +
                            (acceptAll ? "已接受全部修订。" : "")));
                }

                case "status":
                {
                    bool isTracking = doc.TrackRevisions;
                    int revisionCount = doc.Revisions.Count;
                    return Task.FromResult(
                        ToolExecutionResult.Ok(
                            $"修订追踪: {(isTracking ? "已开启" : "已关闭")}，" +
                            $"待处理修订: {revisionCount} 条"));
                }

                default:
                    throw new ToolArgumentException($"未知 action: {action}，可选: on, off, status");
            }
        }
    }
}
