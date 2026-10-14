namespace Yaya.Core.Monitoring;

/// <summary>
/// “进程名 → 应用类别”映射表（纯数据 + 纯逻辑，可单测）。
/// 隐私红线：输入只是前台窗口的进程名与标题文本，不读路径、URL 或屏幕内容。
/// 浏览器等宿主进程看不出用途，退回用窗口标题关键词细分（标题仍在红线允许范围内）。
/// 词表为占位初值，TODO(校准)：逐步扩充，后续支持用户自定义。
/// </summary>
public static class AppCatalog
{
    // 学习类：文档 / 课件 / 笔记 / 编程 IDE；HashSet + OrdinalIgnoreCase = 大小写不敏感匹配
    private static readonly HashSet<string> StudyProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WINWORD", "POWERPNT", "EXCEL", "ONENOTE",
        "Code", "devenv", "idea64", "pycharm64", "webstorm64", "notepad++",
        "AcroRd32", "Acrobat", "CAJViewer", "XMind", "Anki",
    };

    // 娱乐类：游戏平台 / 流媒体 / 播放器
    private static readonly HashSet<string> EntertainmentProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "WeGame", "LeagueClient",
        "NeteaseCloudMusic", "QQMusic", "potplayer", "VLC",
    };

    // 社交类
    private static readonly HashSet<string> SocialProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WeChat", "Weixin", "QQ", "TIM", "Telegram",
    };

    // 工具类：中性生产力工具，状态机判定上并入学习侧
    private static readonly HashSet<string> UtilityProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "calc", "explorer", "mspaint", "Everything",
    };

    // 宿主进程：进程名无法体现用途，必须退回标题关键词兜底
    private static readonly HashSet<string> BrowserHostProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "iexplore",
        "360se", "360chrome", "sogouexplorer", "QQBrowser", "opera",
    };

    // 标题兜底关键词（仅对宿主进程生效）；顺序即优先级：娱乐 > 社交 > 学习
    private static readonly string[] EntertainmentKeywords =
    {
        "哔哩哔哩", "Bilibili", "YouTube", "游戏", "抖音", "Steam", "视频",
    };

    private static readonly string[] SocialKeywords =
    {
        "微信", "WeChat", "QQ", "Telegram",
    };

    private static readonly string[] StudyKeywords =
    {
        "课程", "教程", "网课", "学习", "文档", "论文", "知网", "PDF",
    };

    /// <summary>按进程名归类；宿主进程退回标题兜底；完全无法识别时返回 Unknown（不猜）。</summary>
    public static AppCategory Classify(string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return AppCategory.Unknown;
        }

        if (StudyProcesses.Contains(processName)) return AppCategory.Study;
        if (EntertainmentProcesses.Contains(processName)) return AppCategory.Entertainment;
        if (SocialProcesses.Contains(processName)) return AppCategory.Social;
        if (UtilityProcesses.Contains(processName)) return AppCategory.Utility;

        if (BrowserHostProcesses.Contains(processName))
        {
            return ClassifyByTitle(windowTitle);
        }

        return AppCategory.Unknown;
    }

    // 浏览器标题兜底：看不出用途的标签页一律 Unknown → Uncertain，不打扰
    private static AppCategory ClassifyByTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return AppCategory.Unknown;
        }

        if (ContainsAny(windowTitle, EntertainmentKeywords)) return AppCategory.Entertainment;
        if (ContainsAny(windowTitle, SocialKeywords)) return AppCategory.Social;
        if (ContainsAny(windowTitle, StudyKeywords)) return AppCategory.Study;
        return AppCategory.Unknown;
    }

    private static bool ContainsAny(string text, string[] keywords) =>
        Array.Exists(keywords, k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
