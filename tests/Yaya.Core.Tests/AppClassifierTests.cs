using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class AppClassifierTests
{
    private static CategoryRule ProcessRule(
        string value, RuleCategory category, RuleMatch match = RuleMatch.Exact,
        bool enabled = true, string id = "r-process") =>
        new(id, RuleTarget.Process, match, value, category, false, enabled);

    private static CategoryRule TitleRule(
        string value, RuleCategory category, RuleMatch match = RuleMatch.Contains,
        bool browserOnly = true, bool enabled = true, string id = "r-title") =>
        new(id, RuleTarget.Title, match, value, category, browserOnly, enabled);

    [Fact]
    public void Classify_内置DSH_返回工具不再未知()
    {
        var result = new AppClassifier(null).Classify("DeepSeek Harness", "DeepSeek Harness");

        Assert.Equal(AppCategory.Utility, result.Category);
        Assert.Equal(ClassificationSource.BuiltinProcess, result.Source);
    }

    [Fact]
    public void Classify_未知进程_返回Unknown且不猜()
    {
        var result = new AppClassifier(null).Classify("some_unknown_process", "任意标题");

        Assert.Equal(AppCategory.Unknown, result.Category);
        Assert.Equal(ClassificationSource.Unknown, result.Source);
        Assert.False(result.IsMuted);
        Assert.Contains("不判断", result.Reason);
    }

    [Fact]
    public void Classify_用户进程精确规则_覆盖内置()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("DeepSeek Harness", RuleCategory.Study),
        });

        var result = classifier.Classify("DeepSeek Harness", null);

        Assert.Equal(AppCategory.Study, result.Category);
        Assert.Equal(ClassificationSource.UserProcess, result.Source);
        Assert.Equal("r-process", result.RuleId);
    }

    [Fact]
    public void Classify_用户进程包含规则_命中变体()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("DeepSeek", RuleCategory.Utility, RuleMatch.Contains),
        });

        Assert.Equal(AppCategory.Utility,
            classifier.Classify("DeepSeek Harness", null).Category);
        Assert.Equal(ClassificationSource.Unknown,
            classifier.Classify("unrelated", null).Source);
    }

    [Fact]
    public void Classify_进程名与规则值_大小写和首尾空格不敏感()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("  deepseek   harness ", RuleCategory.Study),
        });

        var result = classifier.Classify("DeepSeek Harness", null);

        Assert.Equal(AppCategory.Study, result.Category);
        Assert.Equal(ClassificationSource.UserProcess, result.Source);
    }

    [Fact]
    public void Classify_标题规则默认只对浏览器生效()
    {
        var classifier = new AppClassifier(new[]
        {
            TitleRule("知网", RuleCategory.Study, browserOnly: true),
        });

        // QQ 是内置社交；标题规则不应对非浏览器生效
        var desktop = classifier.Classify("QQ", "知网 - 中国知网");
        Assert.Equal(AppCategory.Social, desktop.Category);

        var browser = classifier.Classify("chrome", "知网 - 中国知网");
        Assert.Equal(AppCategory.Study, browser.Category);
        Assert.Equal(ClassificationSource.UserTitle, browser.Source);
    }

    [Fact]
    public void Classify_标题规则勾选所有窗口_对非浏览器也生效()
    {
        var classifier = new AppClassifier(new[]
        {
            TitleRule("知网", RuleCategory.Study, browserOnly: false),
        });

        var result = classifier.Classify("Notepad", "知网 - 中国知网");

        Assert.Equal(AppCategory.Study, result.Category);
        Assert.Equal(ClassificationSource.UserTitle, result.Source);
    }

    [Fact]
    public void Classify_进程规则整体优先于标题规则()
    {
        var classifier = new AppClassifier(new[]
        {
            TitleRule("知网", RuleCategory.Study, browserOnly: false),
            ProcessRule("QQ", RuleCategory.Utility),
        });

        var result = classifier.Classify("QQ", "知网");

        Assert.Equal(AppCategory.Utility, result.Category);
        Assert.Equal(ClassificationSource.UserProcess, result.Source);
    }

    [Fact]
    public void Classify_同一组内按列表顺序先命中()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("DeepSeek", RuleCategory.Study, RuleMatch.Contains, id: "first"),
            ProcessRule("DeepSeek Harness", RuleCategory.Utility, RuleMatch.Contains, id: "second"),
        });

        var result = classifier.Classify("DeepSeek Harness", null);

        Assert.Equal(AppCategory.Study, result.Category);
        Assert.Equal("first", result.RuleId);
    }

    [Fact]
    public void Classify_禁用规则跳过()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("DeepSeek Harness", RuleCategory.Study, enabled: false),
        });

        var result = classifier.Classify("DeepSeek Harness", null);

        Assert.Equal(AppCategory.Utility, result.Category);
        Assert.Equal(ClassificationSource.BuiltinProcess, result.Source);
    }

    [Fact]
    public void Classify_Ignore规则_映射Unknown且标记不打扰()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule("QQ", RuleCategory.Ignore),
        });

        var result = classifier.Classify("QQ", null);

        Assert.Equal(AppCategory.Unknown, result.Category);
        Assert.True(result.IsMuted);
        Assert.Equal(ClassificationSource.UserProcess, result.Source);
        Assert.Contains("不打扰", result.Reason);
    }

    [Fact]
    public void Classify_空进程名_返回Unknown()
    {
        var result = new AppClassifier(null).Classify("  ", "任意标题");

        Assert.Equal(AppCategory.Unknown, result.Category);
        Assert.Equal(ClassificationSource.Unknown, result.Source);
    }

    [Fact]
    public void Classify_浏览器空标题_返回浏览器兜底()
    {
        var result = new AppClassifier(null).Classify("chrome", "   ");

        Assert.Equal(AppCategory.Unknown, result.Category);
        Assert.Equal(ClassificationSource.BrowserFallback, result.Source);
    }

    [Fact]
    public void Classify_超长标题_截断且不抛()
    {
        var title = new string('a', 5_000);
        var result = new AppClassifier(null).Classify("chrome", title);

        Assert.Equal(ClassificationSource.BrowserFallback, result.Source);
    }

    [Fact]
    public void Classify_超长或空规则值_被丢弃()
    {
        var classifier = new AppClassifier(new[]
        {
            ProcessRule(new string('x', CategoryRule.MaxValueLength + 1), RuleCategory.Study),
            ProcessRule("   ", RuleCategory.Study),
        });

        var result = classifier.Classify("DeepSeek Harness", null);

        Assert.Equal(AppCategory.Utility, result.Category);
        Assert.Equal(ClassificationSource.BuiltinProcess, result.Source);
    }

    [Fact]
    public void Classify_标题精确匹配()
    {
        var classifier = new AppClassifier(new[]
        {
            TitleRule("第 3 章 线性表", RuleCategory.Study, RuleMatch.Exact, browserOnly: true),
        });

        Assert.Equal(AppCategory.Study, classifier.Classify("chrome", "第 3 章 线性表").Category);
        Assert.Equal(ClassificationSource.BrowserFallback,
            classifier.Classify("chrome", "第 3 章 线性表 - 补充").Source);
    }

    [Fact]
    public void Sanitize_保持顺序并丢弃非法项()
    {
        var rules = new[]
        {
            ProcessRule("a", RuleCategory.Study),
            new CategoryRule("bad", (RuleTarget)99, RuleMatch.Exact, "x", RuleCategory.Study, false, true),
            ProcessRule("b", RuleCategory.Utility),
            ProcessRule("", RuleCategory.Study),
        };

        var sanitized = AppRules.Sanitize(rules);

        Assert.Equal(2, sanitized.Count);
        Assert.Equal("a", sanitized[0].Value);
        Assert.Equal("b", sanitized[1].Value);
    }

    [Fact]
    public void Sanitize_规则条数上限()
    {
        var rules = Enumerable.Range(0, AppRules.MaxRules + 5)
            .Select(i => ProcessRule($"p{i}", RuleCategory.Study, id: $"r{i}"))
            .ToArray();

        Assert.Equal(AppRules.MaxRules, AppRules.Sanitize(rules).Count);
    }
}
