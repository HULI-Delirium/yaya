using System.Windows.Threading;
using Yaya.Core.Monitoring;

namespace Yaya.App.Services;

/// <summary>
/// 番茄钟（App 层计时服务）：专注/休息相位经 Func 注入 MonitoringLoop，
/// Core 状态机据此在休息段放行娱乐（R6）。服务零 IO：完成/中断以事件抛出，由 App 落库与提示。
/// </summary>
public sealed class PomodoroService : IDisposable
{
    private readonly Func<int> _focusMinutes;
    private readonly Func<int> _breakMinutes;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _phaseEndsAt;

    public PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.None;

    public TimeSpan Remaining { get; private set; } = TimeSpan.Zero;

    /// <summary>相位翻转（None / Focus / Break 互切），宠物窗据此切换 Relaxed 摇摆。</summary>
    public event Action<PomodoroPhase>? PhaseChanged;

    /// <summary>专注自然走完（此刻服务已自动切入休息相位）。</summary>
    public event Action? FocusCompleted;

    /// <summary>休息自然走完（服务已回到 None）。</summary>
    public event Action? BreakCompleted;

    /// <summary>番茄被中途停止（手动停止或相位内重启前一个）。</summary>
    public event Action? Interrupted;

    /// <summary>剩余时间每秒刷新（宠物状态条/托盘提示用；服务本身仍零 IO）。</summary>
    public event Action<TimeSpan>? RemainingChanged;

    public PomodoroService(Func<int> focusMinutes, Func<int> breakMinutes)
    {
        _focusMinutes = focusMinutes;
        _breakMinutes = breakMinutes;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>开始一个专注番茄；overrideMinutes 为单次覆盖（「明天开始」的建议分钟），不写用户设置。</summary>
    public void StartFocus(int? overrideMinutes = null)
    {
        Begin(PomodoroPhase.Focus, overrideMinutes ?? Math.Clamp(_focusMinutes(), 1, 120));
    }

    public void StartBreak()
    {
        Begin(PomodoroPhase.Break, Math.Clamp(_breakMinutes(), 1, 120));
    }

    /// <summary>停止当前番茄；正在进行时触发 Interrupted。</summary>
    public void Stop()
    {
        if (CurrentPhase == PomodoroPhase.None)
        {
            return;
        }

        _timer.Stop();
        Remaining = TimeSpan.Zero;
        SetPhase(PomodoroPhase.None);
        RemainingChanged?.Invoke(Remaining);
        Interrupted?.Invoke();
    }

    public void Dispose() => _timer.Stop();

    private void Begin(PomodoroPhase phase, int minutes)
    {
        if (CurrentPhase != PomodoroPhase.None)
        {
            Interrupted?.Invoke();   // 相位内重启：前一个番茄记中断
        }

        _phaseEndsAt = DateTimeOffset.Now.AddMinutes(minutes);
        Remaining = TimeSpan.FromMinutes(minutes);
        SetPhase(phase);
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        Remaining = _phaseEndsAt - DateTimeOffset.Now;
        RemainingChanged?.Invoke(Remaining);
        if (Remaining > TimeSpan.Zero)
        {
            return;
        }

        if (CurrentPhase == PomodoroPhase.Focus)
        {
            FocusCompleted?.Invoke();
            StartBreak();   // 自动进入休息
        }
        else if (CurrentPhase == PomodoroPhase.Break)
        {
            BreakCompleted?.Invoke();
            _timer.Stop();
            Remaining = TimeSpan.Zero;
            SetPhase(PomodoroPhase.None);
        }
    }

    private void SetPhase(PomodoroPhase phase)
    {
        CurrentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
