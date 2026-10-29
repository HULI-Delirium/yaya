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
        "DeepSeek Harness",   // 本地 AI 编码工具：开箱不再 Unknown；用户规则仍可覆盖
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

    /// <summary>是否浏览器宿主进程（标题兜底/站点词只对它们生效）。</summary>
    public static bool IsBrowserHost(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && BrowserHostProcesses.Contains(processName.Trim());

    /// <summary>内置进程表查询；未命中返回 false（不猜）。用户规则由 AppClassifier 先处理。</summary>
    public static bool TryGetBuiltinProcessCategory(string? processName, out AppCategory category)
    {
        category = AppCategory.Unknown;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = processName.Trim();
        if (StudyProcesses.Contains(name)) { category = AppCategory.Study; return true; }
        if (EntertainmentProcesses.Contains(name)) { category = AppCategory.Entertainment; return true; }
        if (SocialProcesses.Contains(name)) { category = AppCategory.Social; return true; }
        if (UtilityProcesses.Contains(name)) { category = AppCategory.Utility; return true; }
        return false;
    }

    /// <summary>
    /// 兼容入口：仅内置表 + 旧标题关键词（无用户规则）。新代码请用 <see cref="AppClassifier"/>；
    /// 阶段 2 会把本方法改为内置分类器的薄包装。
    /// </summary>
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
