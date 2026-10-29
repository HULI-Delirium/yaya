using System.Text;

namespace Yaya.Core.Monitoring;

/// <summary>用户规则作用对象：进程名或窗口标题。</summary>
public enum RuleTarget
{
    Process = 0,
    Title = 1,
}

/// <summary>匹配方式：全等或包含（均大小写不敏感）。</summary>
public enum RuleMatch
{
    Exact = 0,
    Contains = 1,
}

/// <summary>规则类别。Ignore 表示「不打扰」：映射 Unknown，不计入学习也不提醒。</summary>
public enum RuleCategory
{
    Study = 0,
    Utility = 1,
    Entertainment = 2,
    Social = 3,
    Ignore = 4,
}

/// <summary>
/// 一条用户分类规则（纯数据，可序列化）。只保存用户输入的字符串，不保存标题/路径/URL。
/// 顺序由调用方列表顺序表达（数组顺序即优先级），不额外存 order 字段。
/// </summary>
public sealed record CategoryRule(
    string Id,
    RuleTarget Target,
    RuleMatch Match,
    string Value,
    RuleCategory Category,
    bool BrowserOnly,
    bool Enabled)
{
    public const int MaxValueLength = 128;

    /// <summary>归一化后的值是否可参与匹配（空值与超长值在加载时丢弃）。</summary>
    public bool IsValid
    {
        get
        {
            var value = RuleText.Normalize(Value);
            return value.Length is > 0 and <= MaxValueLength
                && Enum.IsDefined(Target)
                && Enum.IsDefined(Match)
                && Enum.IsDefined(Category);
        }
    }

    /// <summary>trim + 折叠空白；空 Id 生成新 Guid；进程规则不使用 BrowserOnly。</summary>
    public CategoryRule Normalize() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
        Value = RuleText.Normalize(Value),
        BrowserOnly = Target == RuleTarget.Title && BrowserOnly,
    };
}

/// <summary>规则集合的加载期清洗：归一化、丢弃非法项、限流；顺序保持不变。</summary>
public static class AppRules
{
    /// <summary>规则条数上限（防止异常文件拖慢每一轮 2 秒的匹配）。</summary>
    public const int MaxRules = 100;

    public static IReadOnlyList<CategoryRule> Sanitize(IEnumerable<CategoryRule>? rules)
    {
        if (rules is null)
        {
            return Array.Empty<CategoryRule>();
        }

        var result = new List<CategoryRule>();
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            var normalized = rule.Normalize();
            if (!normalized.IsValid)
            {
                continue;
            }

            result.Add(normalized);
            if (result.Count >= MaxRules)
            {
                break;
            }
        }

        return result;
    }
}

/// <summary>匹配文本的归一化：trim + 连续空白折叠；标题额外截断，防异常长文本。</summary>
internal static class RuleText
{
    public const int MaxTitleLength = 512;

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var builder = new StringBuilder(trimmed.Length);
        var lastWasWhiteSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhiteSpace)
                {
                    builder.Append(' ');
                    lastWasWhiteSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasWhiteSpace = false;
        }

        return builder.ToString();
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var truncated = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
        return Normalize(truncated);
    }
}
