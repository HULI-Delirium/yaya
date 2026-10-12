using Yaya.Core.Services;

namespace Yaya.Core.Tests;

public class DailySummaryComposerTests
{
    [Fact]
    public void Compose_任意输入_固定输出三句话()
    {
        var summary = DailySummaryComposer.Compose(focusedMinutes: 120, distractedMinutes: 30, correctionCount: 0);

        Assert.Equal(3, summary.Count(c => c == '。'));
    }

    [Fact]
    public void Compose_游离超过学习_给出收敛建议()
    {
        var summary = DailySummaryComposer.Compose(focusedMinutes: 30, distractedMinutes: 90, correctionCount: 0);

        Assert.Contains("先收起跑偏的应用", summary);
    }

    [Fact]
    public void Compose_学习不少于游离_给出鼓励()
    {
        var summary = DailySummaryComposer.Compose(focusedMinutes: 90, distractedMinutes: 30, correctionCount: 0);

        Assert.Contains("继续保持", summary);
    }

    [Fact]
    public void Compose_存在纠正记录_回执纠正次数()
    {
        var summary = DailySummaryComposer.Compose(focusedMinutes: 60, distractedMinutes: 10, correctionCount: 2);

        Assert.Contains("纠正了我 2 次", summary);
        Assert.Equal(3, summary.Count(c => c == '。'));
    }
}
