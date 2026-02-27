using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuXing
{
    /// <summary>
    /// Query the enterprise standard database to verify whether referenced standards exist and are valid.
    /// The LLM extracts standard names from the document and passes them as parameters.
    /// Results are returned as structured text for the LLM to interpret and present.
    /// </summary>
    public class CheckStandardTool : ToolBase
    {
        public override string Name => "check_standard";
        public override string DisplayName => "标准规范校验";
        public override ToolCategory Category => ToolCategory.Query;

        public override string Description =>
            "Query the enterprise standard database to verify referenced standards. " +
            "Pass an array of standard identifiers (e.g. [\"GB/T 20000.1-2014\", \"ISO 9001\"]) " +
            "and the tool returns the query result for each standard. " +
            "Use this when the user asks to check whether standards cited in the document are valid/current.";

        public override JObject Parameters => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["standard_names"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" },
                    ["description"] = "Array of standard identifiers to query, " +
                        "e.g. [\"GB/T 20000.1-2014\", \"HB 5469-1991\"]. " +
                        "Extract these from the document text. " +
                        "Strip surrounding punctuation but keep the standard number intact."
                }
            },
            ["required"] = new JArray("standard_names")
        };

        public override Task<ToolExecutionResult> ExecuteAsync(Connect connect, JObject arguments)
        {
            var namesToken = arguments["standard_names"] as JArray;
            if (namesToken == null || namesToken.Count == 0)
                return Task.FromResult(ToolExecutionResult.Fail("standard_names is required and must be a non-empty array"));

            var networkHelper = new NetWorkHelper();
            var sb = new StringBuilder();
            int total = namesToken.Count;
            int successCount = 0;

            for (int i = 0; i < namesToken.Count; i++)
            {
                string name = namesToken[i]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                sb.AppendLine($"### [{i + 1}/{total}] {name}");

                string result = networkHelper.SendStandardCheckRequest(name);
                if (!string.IsNullOrEmpty(result))
                {
                    sb.AppendLine(result.Trim());
                    successCount++;
                }
                else
                {
                    sb.AppendLine("(No results returned)");
                }
                sb.AppendLine();
            }

            sb.Insert(0, $"Standard verification completed: queried {total} standards, {successCount} returned results.\n\n");

            return Task.FromResult(ToolExecutionResult.Ok(sb.ToString()));
        }
    }
}
