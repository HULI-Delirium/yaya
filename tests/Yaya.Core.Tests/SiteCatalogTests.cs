using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class SiteCatalogTests
{
    [Fact]
    public void Entries_首版62条且关键词不重复()
    {
        Assert.Equal(62, SiteCatalog.Entries.Count);
        Assert.Equal(SiteCatalog.Entries.Count,
            SiteCatalog.Entries.Select(e => e.Keyword).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("哔哩哔哩 - 番剧", AppCategory.Entertainment)]
    [InlineData("GitHub - 项目", AppCategory.Study)]
    [InlineData("知网 - 文献", AppCategory.Study)]
    [InlineData("微博 - 热搜", AppCategory.Social)]
    [InlineData("淘宝 - 购物车", AppCategory.Entertainment)]
    [InlineData("Notion - 工作区", AppCategory.Utility)]
    public void TryMatch_站点词映射类别(string title, AppCategory expected)
    {
        Assert.True(SiteCatalog.TryMatch(title, out var entry));
        Assert.Equal(expected, entry.Category);
    }

    [Fact]
    public void TryMatch_微信读书先于微信()
    {
        Assert.True(SiteCatalog.TryMatch("微信读书 - 书架", out var entry));
        Assert.Equal("微信读书", entry.Keyword);
        Assert.Equal(AppCategory.Study, entry.Category);
    }

    [Fact]
    public void Entries_按词长降序()
    {
        var keywords = SiteCatalog.Entries.Select(e => e.Keyword).ToArray();
        for (var i = 1; i < keywords.Length; i++)
        {
            Assert.True(keywords[i - 1].Length >= keywords[i].Length,
                $"{keywords[i - 1]} 应不短于 {keywords[i]}");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("第 3 章 线性表")]
    public void TryMatch_无站名_不命中(string? title)
    {
        Assert.False(SiteCatalog.TryMatch(title, out _));
    }
}
