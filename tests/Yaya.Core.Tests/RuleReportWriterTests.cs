using Yaya.Core.Services;

namespace Yaya.Core.Tests;

public class RuleReportWriterTests
{
    private static readonly DateOnly Day = new(2026, 10, 18);

    [Fact]
    public void Write_正常数据_输出三句话快照()
    {
        var data = new DailyReportData(Day, 87,
            TimeSpan.FromHours(15) + TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(16),
            new TimeOnly(15, 30), 25, 40, 2);

        var text = new RuleReportWriter().Write(data);

        Assert.Equal("今天有效专注 87 分钟。最强时段 15:30–16:00，那个状态值得复制。明天 15:30 先做最难的 25 分钟。", text);
        Assert.Equal(3, text.Count(c => c == '。'));
    }

    [Fact]
    public void Write_有专注但最强窗后无起点_建议用默认话术()
    {
        var data = new DailyReportData(Day, 32,
            TimeSpan.FromHours(10), TimeSpan.FromHours(10) + TimeSpan.FromMinutes(30),
            null, 12, 100, 0);

        var text = new RuleReportWriter().Write(data);

        Assert.Equal("今天有效专注 32 分钟。最强时段 10:00–10:30，开局不错。明天开始时先做最难的 12 分钟。", text);
        Assert.Equal(3, text.Count(c => c == '。'));
    }

    [Fact]
    public void Write_空数据_默认三句话且恰好三句()
    {
        var data = new DailyReportData(Day, 0, null, null, null, 12, 0, 0);

        var text = new RuleReportWriter().Write(data);

        Assert.Equal("今天还没有有效专注记录。最强时段还没诞生，别在意。明天开始时先做最难的 12 分钟。", text);
        Assert.Equal(3, text.Count(c => c == '。'));
    }
}
