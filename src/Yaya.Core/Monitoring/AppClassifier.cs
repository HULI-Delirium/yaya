namespace Yaya.Core.Monitoring;

/// <summary>
/// 分类解析器（纯函数，可单测）：用户进程规则 → 用户标题规则 → 内置进程表 →
/// 内置站点词表 → 宿主兜底 → Unknown。不可变；规则变更时由调用方重建实例。
/// 隐私红线：只消费进程名与标题文本，不读路径、URL、页面内容。
/// </summary>
public sealed class AppClassifier
{
    private readonly CategoryRule[] _processRules;
    private readonly CategoryRule[] _titleRules;

    /// <summary>仅内置表（无用户规则）的解析器；AppCatalog.Classify 兼容入口使用。</summary>
    public static AppClassifier BuiltinOnly { get; } = new(null);

    public AppClassifier(IReadOnlyList<CategoryRule>? userRules)
    {
        var rules = AppRules.Sanitize(userRules);
        _processRules = rules.Where(r => r.Enabled && r.Target == RuleTarget.Process).ToArray();
        _titleRules = rules.Where(r => r.Enabled && r.Target == RuleTarget.Title).ToArray();
    }

    /// <summary>按固定顺序解析；先命中先返回。空进程名直接 Unknown。</summary>
    public ClassificationResult Classify(string? processName, string? windowTitle)
    {
        var process = RuleText.Normalize(processName);
        if (process.Length == 0)
        {
            return ClassificationResult.Unknown("没有前台进程名，无法判断。");
        }

        foreach (var rule in _processRules)
        {
            if (Matches(rule, process))
            {
                return FromRule(rule, ClassificationSource.UserProcess);
            }
        }

        var isBrowser = AppCatalog.IsBrowserHost(process);
        var title = RuleText.NormalizeTitle(windowTitle);

        foreach (var rule in _titleRules)
        {
            if (rule.BrowserOnly && !isBrowser)
            {
                continue;
            }

            if (Matches(rule, title))
            {
                return FromRule(rule, ClassificationSource.UserTitle);
            }
        }

        if (AppCatalog.TryGetBuiltinProcessCategory(process, out var builtin))
        {
            return new ClassificationResult(builtin, ClassificationSource.BuiltinProcess, null, null, false,
                $"内置映射：{process} → {CategoryLabel(builtin)}");
        }

        if (isBrowser)
        {
            // 站点词表在阶段 2 接入；先按「不猜」返回浏览器兜底
            return new ClassificationResult(AppCategory.Unknown, ClassificationSource.BrowserFallback, null, null, false,
                "浏览器标题未命中任何站点词 → 未知（不猜）。");
        }

        return ClassificationResult.Unknown("进程名不在内置表，也不是浏览器 → 不判断、不提醒。");
    }

    private static ClassificationResult FromRule(CategoryRule rule, ClassificationSource source)
    {
        var category = ToCategory(rule.Category);
        var muted = rule.Category == RuleCategory.Ignore;
        var reason = muted
            ? "你把它设为「不打扰」→ 不计入学习，也不提醒。"
            : $"命中你的规则：{TargetLabel(rule.Target)} = {rule.Value} → {CategoryLabel(category)}";
        return new ClassificationResult(category, source, rule.Id, null, muted, reason);
    }

    private static bool Matches(CategoryRule rule, string text) => rule.Match switch
    {
        RuleMatch.Exact => string.Equals(text, rule.Value, StringComparison.OrdinalIgnoreCase),
        RuleMatch.Contains => text.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static AppCategory ToCategory(RuleCategory category) => category switch
    {
        RuleCategory.Study => AppCategory.Study,
        RuleCategory.Utility => AppCategory.Utility,
        RuleCategory.Entertainment => AppCategory.Entertainment,
        RuleCategory.Social => AppCategory.Social,
        _ => AppCategory.Unknown,   // Ignore 与未定义值都按不打扰处理
    };

    private static string TargetLabel(RuleTarget target) =>
        target == RuleTarget.Process ? "进程名" : "标题";

    private static string CategoryLabel(AppCategory category) => category switch
    {
        AppCategory.Study => "学习",
        AppCategory.Utility => "工具",
        AppCategory.Entertainment => "娱乐",
        AppCategory.Social => "社交",
        _ => "未知",
    };
}
