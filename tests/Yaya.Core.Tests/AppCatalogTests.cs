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
    [InlineData("DeepSeek Harness", AppCategory.Utility)]
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
    public void Classify_浏览器宿主进程_按站点词细分()
    {
        Assert.Equal(AppCategory.Entertainment,
            AppCatalog.Classify("chrome", "哔哩哔哩 (゜-゜)つロ 干杯~-bilibili"));
        Assert.Equal(AppCategory.Study,
            AppCatalog.Classify("msedge", "中国大学MOOC - 高等数学"));
        Assert.Equal(AppCategory.Social,
            AppCatalog.Classify("firefox", "微信网页版"));
    }

    [Fact]
    public void Classify_浏览器标题先清洗宿主后缀()
    {
        Assert.Equal(AppCategory.Study, AppCatalog.Classify("msedge", "GitHub - Profile 1 - Microsoft Edge"));
        Assert.Equal(AppCategory.Study, AppCatalog.Classify("chrome", "GitHub - Google Chrome"));
    }

    [Fact]
    public void Classify_浏览器标题无站名_返回未知不猜测()
    {
        Assert.Equal(AppCategory.Unknown, AppCatalog.Classify("chrome", "新建标签页"));
        // 旧通用关键词（网课/课程）不再猜测
        Assert.Equal(AppCategory.Unknown, AppCatalog.Classify("msedge", "高等数学 网课 - 课程中心"));
    }

    [Fact]
    public void Classify_微信读书优先于微信()
    {
        Assert.Equal(AppCategory.Study, AppCatalog.Classify("chrome", "微信读书 - 书架"));
        Assert.Equal(AppCategory.Social, AppCatalog.Classify("chrome", "微信网页版"));
    }
}
