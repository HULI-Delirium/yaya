using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class AppCategoryClassifierTests
{
    [Theory]
    [InlineData("高等数学 - Word", AppCategory.Study)]
    [InlineData("知网研学 - 文献阅读", AppCategory.Study)]
    [InlineData("Steam 商店", AppCategory.Entertainment)]
    [InlineData("哔哩哔哩 (゜-゜)つロ 干杯~-bilibili", AppCategory.Entertainment)]
    [InlineData("STEAM", AppCategory.Entertainment)]
    [InlineData("微信", AppCategory.Social)]
    [InlineData("Visual Studio", AppCategory.Work)]
    [InlineData("无标题 - 记事本", AppCategory.Unknown)]
    public void Classify_已知关键词_归入对应类别(string title, AppCategory expected)
    {
        Assert.Equal(expected, AppCategoryClassifier.Classify(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_空标题_返回未知(string? title)
    {
        Assert.Equal(AppCategory.Unknown, AppCategoryClassifier.Classify(title));
    }
}
