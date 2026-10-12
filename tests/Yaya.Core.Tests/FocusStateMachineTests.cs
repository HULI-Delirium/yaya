using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

public class FocusStateMachineTests
{
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => UtcNowValue;
    }

    private static FocusSample Sample(AppCategory category, double idleSeconds) =>
        new(category, idleSeconds, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Evaluate_学习窗口且未空闲_转入有效学习且不打扰()
    {
        var machine = new FocusStateMachine(new FakeClock());

        var transition = machine.Evaluate(Sample(AppCategory.Study, 0));

        Assert.Equal(FocusState.Focused, transition.To);
        Assert.Equal(0.9, transition.Confidence);
        Assert.False(transition.ShouldNotify);
    }

    [Fact]
    public void Evaluate_娱乐窗口_转入游离并提醒()
    {
        var machine = new FocusStateMachine(new FakeClock());

        var transition = machine.Evaluate(Sample(AppCategory.Entertainment, 0));

        Assert.Equal(FocusState.Distracted, transition.To);
        Assert.True(transition.Confidence >= MonitoringThresholds.NotifyMinConfidence);
        Assert.True(transition.ShouldNotify);
    }

    [Fact]
    public void Evaluate_空闲超过阈值_高置信度转入游离()
    {
        var machine = new FocusStateMachine(new FakeClock());

        var transition = machine.Evaluate(Sample(AppCategory.Unknown, MonitoringThresholds.IdleThresholdSeconds + 1));

        Assert.Equal(FocusState.Distracted, transition.To);
        Assert.Equal(1.0, transition.Confidence);
        Assert.True(transition.ShouldNotify);
    }

    [Fact]
    public void Evaluate_标题未知_转入不确定且低置信度不打扰()
    {
        var machine = new FocusStateMachine(new FakeClock());

        var transition = machine.Evaluate(Sample(AppCategory.Unknown, 0));

        Assert.Equal(FocusState.Uncertain, transition.To);
        Assert.True(transition.Confidence < MonitoringThresholds.NotifyMinConfidence);
        Assert.False(transition.ShouldNotify);
    }

    [Fact]
    public void Evaluate_持续游离_不重复打扰()
    {
        var machine = new FocusStateMachine(new FakeClock());

        var first = machine.Evaluate(Sample(AppCategory.Entertainment, 0));
        var second = machine.Evaluate(Sample(AppCategory.Entertainment, 1));

        Assert.True(first.ShouldNotify);
        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void Evaluate_冷却期内再次游离_不打扰_冷却后恢复提醒()
    {
        var clock = new FakeClock { UtcNowValue = DateTimeOffset.UnixEpoch };
        var machine = new FocusStateMachine(clock);

        // 第一次游离：提醒
        Assert.True(machine.Evaluate(Sample(AppCategory.Entertainment, 0)).ShouldNotify);

        // 回到学习，再快速跑偏：冷却期内，不打扰
        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(1);
        machine.Evaluate(Sample(AppCategory.Study, 0));
        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(1);
        var withinCooldown = machine.Evaluate(Sample(AppCategory.Entertainment, 1));
        Assert.False(withinCooldown.ShouldNotify);

        // 推进时间超过冷却阈值：恢复提醒
        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(MonitoringThresholds.NudgeCooldownMinutes + 1);
        machine.Evaluate(Sample(AppCategory.Study, 0));
        var afterCooldown = machine.Evaluate(Sample(AppCategory.Entertainment, 1));
        Assert.True(afterCooldown.ShouldNotify);
    }

    [Fact]
    public void CorrectTo_用户纠正_返回原状态并切换当前态()
    {
        var machine = new FocusStateMachine(new FakeClock());
        machine.Evaluate(Sample(AppCategory.Entertainment, 0));

        var original = machine.CorrectTo(FocusState.Focused);

        Assert.Equal(FocusState.Distracted, original);
        Assert.Equal(FocusState.Focused, machine.Current);
    }
}
