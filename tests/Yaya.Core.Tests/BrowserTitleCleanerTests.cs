using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class BrowserTitleCleanerTests
{
    [Theory]
    [InlineData("GitHub - Google Chrome", "GitHub")]
    [InlineData("第 3 章 线性表 - Google Chrome", "第 3 章 线性表")]
    [InlineData("GitHub - Profile 1 - Microsoft Edge", "GitHub")]
    [InlineData("第 3 章 线性表 - 个人 - Microsoft Edge", "第 3 章 线性表")]
    [InlineData("第 3 章 线性表 — Mozilla Firefox", "第 3 章 线性表")]
    [InlineData("GitHub - Brave", "GitHub")]
    [InlineData("GitHub - QQ浏览器", "GitHub")]
    [InlineData("第 3 章 - 线性表 - Google Chrome", "第 3 章 - 线性表")]
    public void Clean_去宿主后缀保留页面标题(string input, string expected)
    {
        Assert.Equal(expected, BrowserTitleCleaner.Clean(input));
    }

    [Fact]
    public void Clean_无后缀_原样返回()
    {
        Assert.Equal("第 3 章 线性表", BrowserTitleCleaner.Clean("第 3 章 线性表"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_空标题_返回空串(string? input)
    {
        Assert.Equal(string.Empty, BrowserTitleCleaner.Clean(input));
    }

    [Fact]
    public void Clean_零宽字符不参与匹配()
    {
        Assert.Equal("GitHub", BrowserTitleCleaner.Clean("Git\u200BHub - Google Chrome"));
    }
}
