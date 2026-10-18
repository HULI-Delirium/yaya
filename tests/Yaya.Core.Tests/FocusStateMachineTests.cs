using Yaya.Core.Monitoring;

namespace Yaya.Core.Tests;

/// <summary>
/// 状态机 v2 全迁移路径用例。时钟一律注入手工写的 FakeClock（可拨表），
/// 全程无 Thread.Sleep（CONVENTIONS：Core 行为变更必须配测试）。
/// </summary>
public class FocusStateMachineTests
{
    /// <summary>手工假时钟：生产用 TimeProvider.System，测试里拨这个。</summary>
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => UtcNowValue;
    }

    // 冷却/纠正类测试用：把“前台切换宽限期”压到 1 毫秒，避免干扰被测逻辑；
    // 凡是切换了类别的相邻两次 Evaluate，时钟至少推进 2 毫秒即可视为宽限已过
    private static FocusStateMachineOptions FastOptions => new()
    {
        CategorySwitchGraceMilliseconds = 1,
    };

    private static FocusStateMachine CreateMachine(FakeClock clock, FocusStateMachineOptions? options = null) =>
        new(options ?? new FocusStateMachineOptions(), clock);

    // ---------- 基础判定（每条迁移路径至少一例） ----------

    [Fact]
    public void Evaluate_首次学习类样本_转入有效学习且不询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock);

        var t = machine.Evaluate(new Observation(AppCategory.Study, 0));

        Assert.Equal(FocusState.Focused, t.To);
        Assert.False(t.ShouldNudge);
        Assert.Equal(FocusState.Focused, machine.Current);
    }

    [Fact]
    public void Evaluate_Utility类别_视同学习类转入有效学习()
    {
        var machine = CreateMachine(new FakeClock());

        var t = machine.Evaluate(new Observation(AppCategory.Utility, 0));

        Assert.Equal(FocusState.Focused, t.To);
    }

    [Fact]
    public void Evaluate_休息相位下学习_仍判有效学习()
    {
        var machine = CreateMachine(new FakeClock());

        var t = machine.Evaluate(new Observation(AppCategory.Study, 0, PomodoroPhase.Focus));

        Assert.Equal(FocusState.Focused, t.To);
    }

    [Fact]
    public void Evaluate_娱乐类别_转入游离并触发询问()
    {
        var machine = CreateMachine(new FakeClock(), FastOptions);

        var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));

        Assert.Equal(FocusState.Distracted, t.To);
        Assert.True(t.ShouldNudge);
        Assert.Equal(NudgeKind.ReturnToStudy, t.NudgeKind);
    }

    [Fact]
    public void Evaluate_社交类别_转入游离并触发询问()
    {
        var machine = CreateMachine(new FakeClock(), FastOptions);

        var t = machine.Evaluate(new Observation(AppCategory.Social, 0));

        Assert.Equal(FocusState.Distracted, t.To);
        Assert.True(t.ShouldNudge);
    }

    [Fact]
    public void Evaluate_未知类别_转入不确定且不询问()
    {
        var machine = CreateMachine(new FakeClock(), FastOptions);

        var t = machine.Evaluate(new Observation(AppCategory.Unknown, 0));

        Assert.Equal(FocusState.Uncertain, t.To);
        Assert.False(t.ShouldNudge);
    }

    // ---------- 信号冲突 → Uncertain（低置信度不打扰红线） ----------

    [Fact]
    public void Evaluate_学习窗口空闲超阈值_信号冲突转不确定()
    {
        var machine = CreateMachine(new FakeClock());   // 默认阈值 120_000ms

        var t = machine.Evaluate(new Observation(AppCategory.Study, 120_001));

        Assert.Equal(FocusState.Uncertain, t.To);
        Assert.False(t.ShouldNudge);
    }

    [Fact]
    public void Evaluate_空闲恰等于阈值_按信号冲突处理()
    {
        var machine = CreateMachine(new FakeClock());

        var t = machine.Evaluate(new Observation(AppCategory.Study, 120_000));

        Assert.Equal(FocusState.Uncertain, t.To);
    }

    [Fact]
    public void Evaluate_学习中超时空闲_从有效学习转不确定()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock);

        machine.Evaluate(new Observation(AppCategory.Study, 0));
        var t = machine.Evaluate(new Observation(AppCategory.Study, 120_001));

        Assert.Equal(FocusState.Focused, t.From);
        Assert.Equal(FocusState.Uncertain, t.To);
        Assert.False(t.ShouldNudge);
    }

    [Fact]
    public void Evaluate_休息相位下娱乐_合法休息转不确定()
    {
        var machine = CreateMachine(new FakeClock());

        var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0, PomodoroPhase.Break));

        Assert.Equal(FocusState.Uncertain, t.To);
        Assert.False(t.ShouldNudge);
    }

    [Fact]
    public void Evaluate_持续不确定_永不发出询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        for (var i = 0; i < 3; i++)
        {
            clock.UtcNowValue += TimeSpan.FromMinutes(1);
            var t = machine.Evaluate(new Observation(AppCategory.Unknown, 0));
            Assert.False(t.ShouldNudge);
        }
    }

    // ---------- 前台切换宽限期 ----------

    [Fact]
    public void Evaluate_前台刚切换_宽限期内保持不确定且不询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock);   // 默认宽限期 8s

        machine.Evaluate(new Observation(AppCategory.Study, 0));            // Focused
        clock.UtcNowValue += TimeSpan.FromSeconds(1);
        var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));  // 刚切换

        Assert.Equal(FocusState.Focused, t.From);
        Assert.Equal(FocusState.Uncertain, t.To);
        Assert.False(t.ShouldNudge);   // 宽限期内即使娱乐类也不打扰
    }

    [Fact]
    public void Evaluate_恰满宽限期_立即恢复判定并询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock);

        machine.Evaluate(new Observation(AppCategory.Study, 0));
        clock.UtcNowValue += TimeSpan.FromSeconds(1);
        machine.Evaluate(new Observation(AppCategory.Entertainment, 0));    // 切换发生
        clock.UtcNowValue += TimeSpan.FromSeconds(8);                       // 距切换恰 8s = 宽限期

        var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));

        Assert.Equal(FocusState.Uncertain, t.From);
        Assert.Equal(FocusState.Distracted, t.To);
        Assert.True(t.ShouldNudge);
    }

    [Fact]
    public void Evaluate_游离后切回学习_经宽限期稳定后回到有效学习()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Entertainment, 0));    // Distracted
        clock.UtcNowValue += TimeSpan.FromMilliseconds(2);
        var mid = machine.Evaluate(new Observation(AppCategory.Study, 0));  // 刚切换 → Uncertain
        clock.UtcNowValue += TimeSpan.FromMilliseconds(2);
        var t = machine.Evaluate(new Observation(AppCategory.Study, 0));    // 宽限已过

        Assert.Equal(FocusState.Uncertain, mid.To);
        Assert.Equal(FocusState.Uncertain, t.From);
        Assert.Equal(FocusState.Focused, t.To);
    }

    // ---------- 询问门控：持续不重复 / 冷却期内不重复 / 冷却后可恢复 ----------

    [Fact]
    public void Evaluate_持续游离_状态不变不重复询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        var first = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));
        clock.UtcNowValue += TimeSpan.FromMinutes(1);
        var second = machine.Evaluate(new Observation(AppCategory.Entertainment, 1_000));

        Assert.True(first.ShouldNudge);
        Assert.Equal(FocusState.Distracted, second.To);
        Assert.False(second.ShouldNudge);
    }

    [Fact]
    public void Evaluate_冷却期内再次进入游离_同类不询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        var first = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));   // 询问（t0）
        machine.MarkAsValid();                                                          // 回退 Focused
        clock.UtcNowValue += TimeSpan.FromMinutes(5);                                   // 冷却期内

        var second = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));

        Assert.True(first.ShouldNudge);
        Assert.Equal(FocusState.Distracted, second.To);
        Assert.False(second.ShouldNudge);   // 距上次询问仅 5 分钟 < 10 分钟
    }

    [Fact]
    public void Evaluate_冷却结束后再次游离_恢复询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Entertainment, 0));   // 询问（t0）
        machine.MarkAsValid();
        clock.UtcNowValue += TimeSpan.FromMinutes(11);                     // 越过 10 分钟冷却

        var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));

        Assert.True(t.ShouldNudge);
    }

    // ---------- 用户纠正 ----------

    [Fact]
    public void MarkAsValid_游离后纠正_回退有效学习并返回记录()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Entertainment, 0));   // Distracted

        var record = machine.MarkAsValid("测试备注");

        Assert.Equal(FocusState.Focused, machine.Current);
        Assert.Equal(UserCorrectionKind.CountAsValid, record.Kind);
        Assert.Equal(FocusState.Distracted, record.OriginalState);
        Assert.Equal(FocusState.Focused, record.ResultingState);
        Assert.Equal("测试备注", record.Note);
        Assert.Equal(clock.UtcNowValue, record.AtUtc);
    }

    [Fact]
    public void MarkEscape_标记逃避_进入微重启且窗口内不询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Study, 0));           // Focused

        var record = machine.MarkEscape();
        var during = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));

        Assert.True(machine.InMicroRestart);
        Assert.Equal(FocusState.Uncertain, machine.Current);
        Assert.Equal(UserCorrectionKind.Escape, record.Kind);
        Assert.Equal(FocusState.Focused, record.OriginalState);
        Assert.Equal(FocusState.Uncertain, during.To);
        Assert.False(during.ShouldNudge);   // 微重启窗口内一律不打扰
    }

    [Fact]
    public void Evaluate_微重启窗口结束_恢复判定()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Study, 0));
        machine.MarkEscape();
        clock.UtcNowValue += TimeSpan.FromMinutes(2);   // 恰满 2 分钟窗口

        Assert.False(machine.InMicroRestart);
        var t = machine.Evaluate(new Observation(AppCategory.Study, 0));
        Assert.Equal(FocusState.Focused, t.To);
    }

    [Fact]
    public void MarkAsValid_微重启中纠正_立即结束微重启()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.MarkEscape();
        machine.MarkAsValid();

        Assert.False(machine.InMicroRestart);
        Assert.Equal(FocusState.Focused, machine.Current);
    }

    // ---------- 番茄钟相位信号（PomodoroService 接入后由动态相位驱动） ----------

    [Fact]
    public void Evaluate_休息相位内持续娱乐_保持不确定且永不询问()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        for (var i = 0; i < 3; i++)
        {
            clock.UtcNowValue += TimeSpan.FromMinutes(1);
            var t = machine.Evaluate(new Observation(AppCategory.Entertainment, 0, PomodoroPhase.Break));
            Assert.Equal(FocusState.Uncertain, t.To);
            Assert.False(t.ShouldNudge);
        }
    }

    [Fact]
    public void Evaluate_游离中相位翻为休息_转入不确定且不询问()
    {
        var machine = CreateMachine(new FakeClock(), FastOptions);

        var first = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));   // Distracted + 询问
        var flipped = machine.Evaluate(new Observation(AppCategory.Entertainment, 0, PomodoroPhase.Break));

        Assert.True(first.ShouldNudge);
        Assert.Equal(FocusState.Distracted, flipped.From);
        Assert.Equal(FocusState.Uncertain, flipped.To);
        Assert.False(flipped.ShouldNudge);
    }

    [Fact]
    public void Evaluate_休息转专注相位_恢复判定且同类冷却仍生效()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        machine.Evaluate(new Observation(AppCategory.Entertainment, 0));               // 询问（t0）
        machine.Evaluate(new Observation(AppCategory.Entertainment, 0, PomodoroPhase.Break));
        var withinCooldown = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));
        Assert.False(withinCooldown.ShouldNudge);                                      // 距上次不足 10 分钟

        machine.MarkAsValid();                                                         // 回退 Focused
        clock.UtcNowValue += TimeSpan.FromMinutes(11);
        var afterCooldown = machine.Evaluate(new Observation(AppCategory.Entertainment, 0));
        Assert.Equal(FocusState.Distracted, afterCooldown.To);
        Assert.True(afterCooldown.ShouldNudge);
    }

    [Fact]
    public void Evaluate_无番茄相位信号_行为与未接入时一致()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock, FastOptions);

        Assert.Equal(FocusState.Distracted, machine.Evaluate(new Observation(AppCategory.Entertainment, 0)).To);
        clock.UtcNowValue += TimeSpan.FromMilliseconds(2);   // 类别切换：先落宽限
        Assert.Equal(FocusState.Uncertain, machine.Evaluate(new Observation(AppCategory.Study, 0)).To);
        clock.UtcNowValue += TimeSpan.FromMilliseconds(2);   // 宽限 settle 后恢复判定
        Assert.Equal(FocusState.Focused, machine.Evaluate(new Observation(AppCategory.Study, 0)).To);
    }

    // ---------- 事件与配置 ----------

    [Fact]
    public void StateChanged_发生迁移时_触发且携带迁移信息()
    {
        var clock = new FakeClock();
        var machine = CreateMachine(clock);
        var raised = new List<FocusTransition>();
        machine.StateChanged += (_, t) => raised.Add(t);

        machine.Evaluate(new Observation(AppCategory.Study, 0));   // Uncertain → Focused
        machine.Evaluate(new Observation(AppCategory.Study, 0));   // 无迁移

        var e = Assert.Single(raised);
        Assert.Equal(FocusState.Uncertain, e.From);
        Assert.Equal(FocusState.Focused, e.To);
    }

    [Fact]
    public void Options_默认构造_各阈值取默认值()
    {
        var options = new FocusStateMachineOptions();

        Assert.Equal(120_000, options.IdleThresholdMilliseconds);
        Assert.Equal(8_000, options.CategorySwitchGraceMilliseconds);
        Assert.Equal(10, options.NudgeCooldownMinutes);
        Assert.Equal(2, options.MicroRestartMinutes);
    }

    [Fact]
    public void Options_非法阈值_赋值即抛异常()
    {
        var options = new FocusStateMachineOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.IdleThresholdMilliseconds = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CategorySwitchGraceMilliseconds = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.NudgeCooldownMinutes = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MicroRestartMinutes = -2);
    }
}
