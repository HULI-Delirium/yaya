namespace Yaya.Core.Monitoring;

/// <summary>
/// 仅凭前台窗口标题做关键词归类。
/// 关键词表为占位初值，TODO(校准)：逐步扩充，后续支持用户自定义词表。
/// </summary>
public static class AppCategoryClassifier
{
    private static readonly string[] StudyKeywords =
    {
        "文档", "论文", "Word", "PDF", "知网", "教程", "课程", "网课", "学习", "笔记", "OneNote",
    };

    private static readonly string[] WorkKeywords =
    {
        "Excel", "PowerPoint", "PPT", "Outlook", "Visual Studio", "VS Code", "IDEA", "代码",
    };

    private static readonly string[] EntertainmentKeywords =
    {
        "游戏", "Bilibili", "哔哩哔哩", "YouTube", "视频", "抖音", "Steam", "音乐", "网易云",
    };

    private static readonly string[] SocialKeywords =
    {
        "微信", "WeChat", "QQ", "Telegram", "微博",
    };

    /// <summary>标题为空或不含任何已知关键词时返回 Unknown（不猜、不乱打扰）。</summary>
    public static AppCategory Classify(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return AppCategory.Unknown;
        }

        if (ContainsAny(windowTitle, StudyKeywords)) return AppCategory.Study;
        if (ContainsAny(windowTitle, WorkKeywords)) return AppCategory.Work;
        if (ContainsAny(windowTitle, EntertainmentKeywords)) return AppCategory.Entertainment;
        if (ContainsAny(windowTitle, SocialKeywords)) return AppCategory.Social;
        return AppCategory.Unknown;
    }

    private static bool ContainsAny(string title, string[] keywords) =>
        Array.Exists(keywords, k => title.Contains(k, StringComparison.OrdinalIgnoreCase));
}
