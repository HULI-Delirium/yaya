using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class AppCatalogTests
{
    [Theory]
    [InlineData("WINWORD", AppCategory.Study)]
    [InlineData("Code", AppCategory.Study)]
    [InlineData("steam", AppCategory.Entertainment)]
    [InlineData("WeChat", AppCategory.Social)]
    [InlineData("calc", AppCategory.Utility)]
    public void Classify_已知进程名_归入对应类别(string processName, AppCategory expected)
    {
        Assert.Equal(expected, AppCatalog.Classify(processName, null));
    }

    [Fact]
    public void Classify_进程名大小写不敏感()
    {
        Assert.Equal(AppCategory.Study, AppCatalog.Classify("winword", null));
        Assert.Equal(AppCategory.Entertainment, AppCatalog.Classify("STEAM", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some_unknown_process")]
    public void Classify_未知进程名_返回未知(string? processName)
    {
        Assert.Equal(AppCategory.Unknown, AppCatalog.Classify(processName, "任意标题"));
    }

    [Fact]
    public void Classify_浏览器宿主进程_退回标题关键词细分()
    {
        Assert.Equal(AppCategory.Entertainment,
            AppCatalog.Classify("chrome", "哔哩哔哩 (゜-゜)つロ 干杯~-bilibili"));
        Assert.Equal(AppCategory.Study,
            AppCatalog.Classify("msedge", "高等数学 网课 - 课程中心"));
        Assert.Equal(AppCategory.Social,
            AppCatalog.Classify("firefox", "微信网页版"));
    }

    [Fact]
    public void Classify_浏览器标题无特征_返回未知不猜测()
    {
        Assert.Equal(AppCategory.Unknown, AppCatalog.Classify("chrome", "新建标签页"));
    }
}
