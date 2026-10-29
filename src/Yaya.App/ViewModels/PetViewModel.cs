using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yaya.App.Pet;
using Yaya.Core.Monitoring;

namespace Yaya.App.ViewModels;

/// <summary>
/// 宠物窗视图模型：状态机事件 → 视觉姿态的映射（只处理 Focused / Distracted / 纠正事件，
/// Uncertain 一律保持当前姿态）。持有眨眼 / 迁移防抖 / 开花彩蛋 / 气泡自闭合四个
/// DispatcherTimer，Dispose 统一停表（资源红线）。
/// </summary>
public partial class PetViewModel : ObservableObject, IDisposable
{
    // 迁移防抖窗口：略大于最长迁移动画（2s），期间忽略新的状态变化
    private static readonly TimeSpan TransitionGuard = TimeSpan.FromMilliseconds(2100);
    private static readonly TimeSpan BloomDuration = TimeSpan.FromSeconds(3);
    private const double BlinkSecondsMin = 3;
    private const double BlinkSecondsMax = 6;
    private const double BlinkHoldMilliseconds = 150;

    private readonly PetAssets _assets;
    private readonly Action<string> _onMarkValid;
    private readonly Action<string> _onMarkEscape;
    private readonly Random _random = new();

    private PetVisualState _frontState = PetVisualState.Healthy;
    private bool _isTransitioning;
    private bool _alert;
    private bool _relaxed;
    private DateTimeOffset? _distractedSince;

    /// <summary>当前姿态 VisualState（Healthy / Wilted / Weeds / Bloom）。</summary>
    [ObservableProperty]
    private string _postureState = "Healthy";

    /// <summary>当前心情 VisualState（Calm 摇摆 / Alert 停止摇摆）。</summary>
    [ObservableProperty]
    private string _moodState = "Calm";

    /// <summary>前层图（新姿态图，交叉淡化的入场层）。</summary>
    [ObservableProperty]
    private ImageSource _petImage;

    /// <summary>后层图（旧姿态图，交叉淡化的退场层）。</summary>
    [ObservableProperty]
    private ImageSource _petBackImage;

    [ObservableProperty]
    private bool _nudgeVisible;

    [ObservableProperty]
    private string _nudgeText = string.Empty;

    /// <summary>番茄状态条是否可见（无番茄时隐藏，不占视觉）。</summary>
    [ObservableProperty]
    private bool _isPomodoroStatusVisible;

    /// <summary>番茄状态条文字：专注 24:31 / 休息 04:12。</summary>
    [ObservableProperty]
    private string _pomodoroStatusText = string.Empty;

    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _blinkRevertTimer;
    private readonly DispatcherTimer _transitionGuardTimer;
    private readonly DispatcherTimer _bloomTimer;
    private readonly DispatcherTimer _nudgeAutoCloseTimer;

    public PetViewModel(PetAssets assets, Action<string> onMarkValid, Action<string> onMarkEscape)
    {
        _assets = assets;
        _onMarkValid = onMarkValid;
        _onMarkEscape = onMarkEscape;

        PetImage = assets.Get(PetVisualState.Healthy);
        PetBackImage = assets.Get(PetVisualState.Healthy);

        _blinkTimer = new DispatcherTimer { Interval = NextBlinkInterval() };
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();
        _blinkRevertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BlinkHoldMilliseconds) };
        _blinkRevertTimer.Tick += OnBlinkRevertTick;
        _transitionGuardTimer = new DispatcherTimer { Interval = TransitionGuard };
        _transitionGuardTimer.Tick += (_, _) =>
        {
            _transitionGuardTimer.Stop();
            _isTransitioning = false;
        };
        _bloomTimer = new DispatcherTimer { Interval = BloomDuration };
        _bloomTimer.Tick += (_, _) =>
        {
            _bloomTimer.Stop();
            NavigateTo("Healthy", PetVisualState.Healthy);
        };
        _nudgeAutoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _nudgeAutoCloseTimer.Tick += (_, _) =>
        {
            _nudgeAutoCloseTimer.Stop();
            NudgeVisible = false;
        };
    }

    /// <summary>状态机迁移入口（App 订阅 MonitoringLoop 后转发）。Uncertain 不触发姿态变化。</summary>
    public void OnFocusTransition(FocusTransition transition)
    {
        // 心情与姿态正交：Alert 只跟状态机走，Relaxed 只跟番茄钟走，互不改写对方
        _alert = transition.To == FocusState.Distracted;
        UpdateMood();

        if (_isTransitioning)
        {
            return;
        }

        switch (transition.To)
        {
            case FocusState.Focused:
                _distractedSince = null;
                NavigateTo("Healthy", PetVisualState.Healthy);
                break;
            case FocusState.Distracted:
                _distractedSince ??= DateTimeOffset.Now;
                EnterWilted();
                break;
            case FocusState.Uncertain:
                break;   // 保持当前姿态
        }
    }

    /// <summary>Core 允许询问时由 App 转发；提醒总开关在 App 层过滤（不影响 Core 冷却计数）。</summary>
    public void OnNudgeRequested()
    {
        var minutes = _distractedSince is { } since
            ? Math.Max(1, (int)Math.Ceiling((DateTimeOffset.Now - since).TotalMinutes))
            : 1;
        NudgeText = $"刚才那 {minutes} 分钟，是查资料还是休息一下？";
        NudgeVisible = true;
        // 10 秒无操作自动淡出（仅隐藏 UI，冷却逻辑全在 Core）
        _nudgeAutoCloseTimer.Stop();
        _nudgeAutoCloseTimer.Start();
    }

    /// <summary>气泡按钮：有效查资料 → 开花彩蛋 3 秒后回落，并把纠正交给 Core。</summary>
    [RelayCommand]
    private void MarkValid()
    {
        CloseNudge();
        _onMarkValid("宠物气泡：刚才算有效");
        NavigateTo("Bloom", PetVisualState.Bloom);
        _bloomTimer.Stop();
        _bloomTimer.Start();
    }

    /// <summary>气泡按钮：在逃避 → 长草（保持到下次 Focused），并把纠正交给 Core。</summary>
    [RelayCommand]
    private void MarkEscape()
    {
        CloseNudge();
        _onMarkEscape("宠物气泡：在逃避");
        NavigateTo("Weeds", PetVisualState.Weeds);
    }

    /// <summary>开发期调试入口（冒烟验收热键 F6/F7 用，DEBUG_HOTKEYS 开启时可用）。</summary>
    public void DevNavigate(FocusState simulated) =>
        OnFocusTransition(new FocusTransition(FocusState.Uncertain, simulated, false, null));

    /// <summary>
    /// 番茄状态条（App 每秒刷新）：相位 + 剩余时间；无番茄时隐藏。
    /// 状态条只是展示，不参与 Core 判定（相位经 PomodoroService 注入状态机）。
    /// </summary>
    public void SetPomodoroStatus(PomodoroPhase phase, TimeSpan remaining)
    {
        if (phase == PomodoroPhase.None)
        {
            IsPomodoroStatusVisible = false;
            PomodoroStatusText = string.Empty;
            return;
        }

        var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        var label = phase == PomodoroPhase.Break ? "休息" : "专注";
        PomodoroStatusText = $"{label} {totalSeconds / 60:00}:{totalSeconds % 60:00}";
        IsPomodoroStatusVisible = true;
    }

    /// <summary>番茄钟休息相位：Relaxed 大幅摇摆（Alert 优先，休息中本就不会有 Alert）。</summary>
    public void SetRelaxed(bool relaxed)
    {
        _relaxed = relaxed;
        UpdateMood();
    }

    private void UpdateMood() =>
        MoodState = _alert ? "Alert" : _relaxed ? "Relaxed" : "Calm";

    public void Dispose()
    {
        _blinkTimer.Stop();
        _blinkRevertTimer.Stop();
        _transitionGuardTimer.Stop();
        _bloomTimer.Stop();
        _nudgeAutoCloseTimer.Stop();
    }

    // 蔫：特殊迁移——back 层放 wilt_a、front 层放 wilt_b，由 Healthy→Wilted 迁移做交叉淡化
    private void EnterWilted()
    {
        SetLayers(PetVisualState.WiltA, PetVisualState.WiltB);
        PostureState = "Wilted";
        ArmTransitionGuard();
    }

    // 通用迁移：旧 front 退到 back 层，新图上 front 层，VSM 默认迁移做 0.6s 交叉淡化
    private void NavigateTo(string posture, PetVisualState front)
    {
        SetLayers(_frontState, front);
        PostureState = posture;
        ArmTransitionGuard();
    }

    private void SetLayers(PetVisualState back, PetVisualState front)
    {
        PetBackImage = _assets.Get(back);
        PetImage = _assets.Get(front);
        _frontState = front;
    }

    private void ArmTransitionGuard()
    {
        _isTransitioning = true;
        _transitionGuardTimer.Stop();
        _transitionGuardTimer.Start();
    }

    private void CloseNudge()
    {
        _nudgeAutoCloseTimer.Stop();
        NudgeVisible = false;
    }

    private TimeSpan NextBlinkInterval() =>
        TimeSpan.FromSeconds(BlinkSecondsMin + _random.NextDouble() * (BlinkSecondsMax - BlinkSecondsMin));

    // 眨眼：随机 3–6s 触发；仅在 Healthy 且非迁移期间；150ms 后换回
    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _blinkTimer.Interval = NextBlinkInterval();
        if (_isTransitioning || PostureState != "Healthy")
        {
            return;
        }

        PetImage = _assets.Get(PetVisualState.Blink);
        _blinkRevertTimer.Stop();
        _blinkRevertTimer.Start();
    }

    private void OnBlinkRevertTick(object? sender, EventArgs e)
    {
        _blinkRevertTimer.Stop();
        if (!_isTransitioning && PostureState == "Healthy")
        {
            PetImage = _assets.Get(PetVisualState.Healthy);
        }
    }
}
