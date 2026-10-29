namespace Yaya.Core.Monitoring;

/// <summary>站点词条：站名/品牌词 → 默认类别。</summary>
public sealed record SiteEntry(string Keyword, AppCategory Category);

/// <summary>
/// 内置浏览器站点词表（首版 62 条，纯数据，可单测）。
/// 只匹配浏览器标题清洗后的文本；词条按长度降序匹配，保证「微信读书」先于「微信」。
/// 红线：不读 URL、不读页面内容；识别不了就返回 Unknown，让用户补规则。
/// </summary>
public static class SiteCatalog
{
    /// <summary>词表（已按词长降序排好；同长保持声明顺序）。</summary>
    public static IReadOnlyList<SiteEntry> Entries { get; } = Build();

    /// <summary>在清洗后的标题里找第一个命中的站点；没有返回 false。</summary>
    public static bool TryMatch(string? cleanedTitle, out SiteEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(cleanedTitle))
        {
            return false;
        }

        foreach (var candidate in Entries)
        {
            if (cleanedTitle.Contains(candidate.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<SiteEntry> Build()
    {
        var entries = new List<SiteEntry>
        {
            // 娱乐（18）
            new("哔哩哔哩", AppCategory.Entertainment),
            new("Bilibili", AppCategory.Entertainment),
            new("B站", AppCategory.Entertainment),
            new("YouTube", AppCategory.Entertainment),
            new("抖音", AppCategory.Entertainment),
            new("快手", AppCategory.Entertainment),
            new("TikTok", AppCategory.Entertainment),
            new("Netflix", AppCategory.Entertainment),
            new("爱奇艺", AppCategory.Entertainment),
            new("腾讯视频", AppCategory.Entertainment),
            new("优酷", AppCategory.Entertainment),
            new("芒果TV", AppCategory.Entertainment),
            new("Twitch", AppCategory.Entertainment),
            new("Steam", AppCategory.Entertainment),
            new("淘宝", AppCategory.Entertainment),   // 购物无枚举，按娱乐近似
            new("京东", AppCategory.Entertainment),
            new("网易云音乐", AppCategory.Entertainment),
            new("QQ音乐", AppCategory.Entertainment),

            // 社交（11）
            new("微博", AppCategory.Social),
            new("知乎", AppCategory.Social),
            new("小红书", AppCategory.Social),
            new("豆瓣", AppCategory.Social),
            new("贴吧", AppCategory.Social),
            new("Reddit", AppCategory.Social),
            new("Twitter", AppCategory.Social),
            new("Facebook", AppCategory.Social),
            new("Instagram", AppCategory.Social),
            new("Discord", AppCategory.Social),
            new("微信", AppCategory.Social),

            // 学习（25）
            new("微信读书", AppCategory.Study),
            new("GitHub", AppCategory.Study),
            new("GitLab", AppCategory.Study),
            new("Stack Overflow", AppCategory.Study),
            new("StackOverflow", AppCategory.Study),
            new("CSDN", AppCategory.Study),
            new("掘金", AppCategory.Study),
            new("LeetCode", AppCategory.Study),
            new("牛客", AppCategory.Study),
            new("Overleaf", AppCategory.Study),
            new("知网", AppCategory.Study),
            new("万方", AppCategory.Study),
            new("维普", AppCategory.Study),
            new("学习通", AppCategory.Study),
            new("超星", AppCategory.Study),
            new("中国大学MOOC", AppCategory.Study),
            new("MOOC", AppCategory.Study),
            new("Coursera", AppCategory.Study),
            new("edX", AppCategory.Study),
            new("Canvas", AppCategory.Study),
            new("Moodle", AppCategory.Study),
            new("arXiv", AppCategory.Study),
            new("Google Scholar", AppCategory.Study),
            new("百度学术", AppCategory.Study),
            new("慕课网", AppCategory.Study),

            // 工具（8）
            new("Notion", AppCategory.Utility),
            new("飞书", AppCategory.Utility),
            new("腾讯文档", AppCategory.Utility),
            new("石墨文档", AppCategory.Utility),
            new("语雀", AppCategory.Utility),
            new("百度网盘", AppCategory.Utility),
            new("ChatGPT", AppCategory.Utility),
            new("Claude", AppCategory.Utility),
        };

        // 稳定排序：词长降序，同长保持声明顺序
        return entries
            .Select((entry, index) => (entry, index))
            .OrderByDescending(x => x.entry.Keyword.Length)
            .ThenBy(x => x.index)
            .Select(x => x.entry)
            .ToArray();
    }
}
