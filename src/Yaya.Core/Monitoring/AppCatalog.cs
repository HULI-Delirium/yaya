namespace Yaya.Core.Monitoring;

/// <summary>
/// 内置“进程名 / 浏览器站点 → 应用类别”映射表（纯数据 + 纯逻辑，可单测）。
/// 用户规则由 <see cref="AppClassifier"/> 在更前一层处理；本类只提供开箱默认值。
/// 隐私红线：输入只是前台窗口的进程名与标题文本，不读路径、URL 或屏幕内容。
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

    // 宿主进程：进程名无法体现用途，退回标题清洗 + 站点词表
    private static readonly HashSet<string> BrowserHostProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "iexplore", "opera", "vivaldi",
        "360se", "360chrome", "sogouexplorer", "QQBrowser", "2345Explorer", "Maxthon",
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
    /// 兼容入口：仅内置表 + 站点词（无用户规则）。新代码请用 <see cref="AppClassifier"/>
    /// 以便接收用户规则；本方法保留给旧测试与外部调用。
    /// </summary>
    public static AppCategory Classify(string? processName, string? windowTitle) =>
        AppClassifier.BuiltinOnly.Classify(processName, windowTitle).Category;
}
