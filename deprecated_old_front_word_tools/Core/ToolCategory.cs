namespace FuXing
{
    /// <summary>
    /// 工具分类枚举，用于在 system prompt 中按功能域组织工具描述，
    /// 以帮助 LLM 更准确地选择合适的工具。
    /// </summary>
    public enum ToolCategory
    {
        /// <summary>信息查询类（不修改文档）</summary>
        Query,

        /// <summary>文本编辑类（插入、替换、删除）</summary>
        Editing,

        /// <summary>格式化类（样式、排版）</summary>
        Formatting,

        /// <summary>结构操作类（章节、大纲、合稿）</summary>
        Structure,

        /// <summary>页面设置类（页面布局、页眉页脚）</summary>
        PageLayout,

        /// <summary>高级工具类（脚本、批量操作）</summary>
        Advanced,

        /// <summary>系统工具类（技能加载等非文档操作）</summary>
        System,
    }
}
