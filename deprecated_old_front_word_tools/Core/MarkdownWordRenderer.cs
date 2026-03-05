using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlToOpenXml;
using Markdig;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;

namespace FuXing.Core
{
    /// <summary>
    /// 将 Markdown 渲染为临时 DOCX 文件，用于后续通过 Word InsertFile 保留富文本结构插入。
    /// </summary>
    public static class MarkdownWordRenderer
    {
        public class MarkdownDefaultFormatProfile
        {
            public string BodyStyle { get; set; }
            public string Heading1Style { get; set; }
            public string Heading2Style { get; set; }
            public string Heading3Style { get; set; }
            public string Heading4Style { get; set; }
            public string Heading5Style { get; set; }
            public string Heading6Style { get; set; }
            public string TableStyle { get; set; }
            public string BodyFontName { get; set; }
            public string BodyFontNameAscii { get; set; }
            public float? BodyFontSize { get; set; }
            public string BodyAlignment { get; set; }
            public string BodyLineSpacingRule { get; set; }
            public float? BodyLineSpacingPt { get; set; }
            public float? BodyFirstLineIndentPt { get; set; }
        }

        public static string RenderToTempDocx(string markdown)
        {
            string normalized = markdown ?? string.Empty;

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            string html = Markdig.Markdown.ToHtml(normalized, pipeline);
            string tempFilePath = Path.Combine(
                Path.GetTempPath(),
                $"fuxing_md_{Guid.NewGuid():N}.docx");

            using (var wordDoc = WordprocessingDocument.Create(tempFilePath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = wordDoc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());

                var converter = new HtmlConverter(mainPart);
                var elements = converter.Parse(html);
                var body = mainPart.Document.Body;

                foreach (var element in elements)
                    body.Append(element);

                mainPart.Document.Save();
            }

            return tempFilePath;
        }

        public static MarkdownDefaultFormatProfile LoadDefaultFormatProfile()
        {
            string addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string profilePath = Path.Combine(addinDir, "skills", "load_default_style", "default_style_profile.json");

            if (!File.Exists(profilePath))
                throw new InvalidOperationException($"未找到默认格式配置文件: {profilePath}");

            JObject json;
            try
            {
                json = JObject.Parse(File.ReadAllText(profilePath));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"默认格式配置解析失败: {ex.Message}");
            }

            var headingStyles = json["heading_styles"] as JObject;
            var bodyFont = json["body_font"] as JObject;
            var table = json["table"] as JObject;

            var sizeToken = bodyFont?["size_pt"];
            float? bodyFontSize = (sizeToken?.Type == JTokenType.Float || sizeToken?.Type == JTokenType.Integer)
                ? (float?)sizeToken.Value<float>()
                : null;

            return new MarkdownDefaultFormatProfile
            {
                BodyStyle = json["body_style"]?.ToString(),
                Heading1Style = headingStyles?["h1"]?.ToString(),
                Heading2Style = headingStyles?["h2"]?.ToString(),
                Heading3Style = headingStyles?["h3"]?.ToString(),
                Heading4Style = headingStyles?["h4"]?.ToString(),
                Heading5Style = headingStyles?["h5"]?.ToString(),
                Heading6Style = headingStyles?["h6"]?.ToString(),
                TableStyle = table?["style_name"]?.ToString(),
                BodyFontName = bodyFont?["name"]?.ToString(),
                BodyFontNameAscii = bodyFont?["name_ascii"]?.ToString(),
                BodyFontSize = bodyFontSize,
                BodyAlignment = bodyFont?["alignment"]?.ToString(),
                BodyLineSpacingRule = bodyFont?["line_spacing_rule"]?.ToString(),
                BodyLineSpacingPt = bodyFont?["line_spacing_pt"]?.Type == JTokenType.Float || bodyFont?["line_spacing_pt"]?.Type == JTokenType.Integer
                    ? bodyFont["line_spacing_pt"].Value<float>()
                    : (float?)null,
                BodyFirstLineIndentPt = bodyFont?["first_line_indent_pt"]?.Type == JTokenType.Float || bodyFont?["first_line_indent_pt"]?.Type == JTokenType.Integer
                    ? bodyFont["first_line_indent_pt"].Value<float>()
                    : (float?)null
            };
        }

        public static void TryDeleteTempFile(string tempFilePath)
        {
            if (string.IsNullOrWhiteSpace(tempFilePath))
                return;

            try
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
            catch
            {
                // 忽略临时文件清理失败，不影响主流程
            }
        }
    }
}
