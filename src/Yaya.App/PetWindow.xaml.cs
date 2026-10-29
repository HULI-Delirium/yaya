using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Yaya.App.Services;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;

namespace Yaya.App;

/// <summary>「为什么」浮层数据快照（只含进程名/类别/原因，不含整条标题）。</summary>
public sealed record WhyInfo(
    string ProcessText,
    string CategoryText,
    string StateText,
    string ReasonText,
    string HintText,
    bool CanSet);

public partial class PetWindow : Window
{
    // 单击/拖动判定阈值（TODO(校准)：真机手感，4px/300ms 起步）
    private const double ClickMoveTolerancePixels = 4;
    private const long ClickMaxDurationMilliseconds = 300;

    private readonly PetViewModel _viewModel;
    private bool _clickThrough;
    private Point _pressPosition;
    private long _pressTicks;
    private bool _dragging;

    /// <summary>菜单「开始/停止专注」：行为由 App 层接线（窗口不持有番茄服务）。</summary>
    public event EventHandler? TogglePomodoroRequested;

    public event EventHandler? DailyReportRequested;

    public event EventHandler? WeeklyReportRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>「把当前应用设为…」：只抛类别，进程名与规则写入由 App 层负责。</summary>
    public event EventHandler<RuleCategory>? SetCurrentCategoryRequested;

    /// <summary>点击 Uncertain 角标，请求打开「为什么」浮层。</summary>
    public event EventHandler? WhyRequested;

    /// <summary>请求打开「选择窗口…」。</summary>
    public event EventHandler? WindowPickerRequested;

    public event EventHandler? HidePetRequested;

    public event EventHandler? ExitRequested;

    public PetWindow(PetViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
        // 关窗即隐藏：程序驻留托盘，退出走托盘菜单
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        // 宠物区手势：≤4px 且 ≤300ms 视为单击（弹菜单），超过阈值才是拖动；
        // 鼠标穿透开启时窗口不接收消息，菜单与拖动都不可用（设置页有说明）
        PetRoot.MouseLeftButtonDown += OnPetMouseLeftButtonDown;
        PetRoot.MouseMove += OnPetMouseMove;
        PetRoot.MouseLeftButtonUp += OnPetMouseLeftButtonUp;

#if DEBUG_HOTKEYS
        // 调试热键默认不编译进正式构建（符号开启方式见 Assets/pet/README.txt）
        KeyDown += OnDevKeyDown;
#endif
    }

    /// <summary>「为什么」浮层是否打开（App 用来决定是否随分类/状态刷新）。</summary>
    public bool IsWhyOpen => WhyPopup.IsOpen;

    /// <summary>显示「为什么」浮层（App 传入当前分类/状态快照）。</summary>
    public void ShowWhy(WhyInfo info)
    {
        ApplyWhy(info);
        WhyPopup.IsOpen = true;
    }

    /// <summary>浮层打开期间刷新内容；未打开时忽略。</summary>
    public void UpdateWhy(WhyInfo info)
    {
        if (WhyPopup.IsOpen)
        {
            ApplyWhy(info);
        }
    }

    private void ApplyWhy(WhyInfo info)
    {
        WhyProcessText.Text = $"前台：{info.ProcessText}";
        WhyCategoryText.Text = $"类别：{info.CategoryText}";
        WhyStateText.Text = $"判定：{info.StateText}";
        WhyReasonText.Text = $"原因：{info.ReasonText}";
        WhyCategoryPanel.Visibility = info.CanSet ? Visibility.Visible : Visibility.Collapsed;
        WhyBrowserHint.Text = info.HintText;
        WhyBrowserHint.Visibility = info.CanSet ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUncertainBadgeClick(object sender, MouseButtonEventArgs e)
    {
        WhyRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnWhySetCategory(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag }
            && Enum.TryParse<RuleCategory>(tag, out var category))
        {
            WhyPopup.IsOpen = false;
            SetCurrentCategoryRequested?.Invoke(this, category);
        }
    }

    private void OnWhyOpenSettings(object sender, RoutedEventArgs e)
    {
        WhyPopup.IsOpen = false;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWhyClose(object sender, RoutedEventArgs e) => WhyPopup.IsOpen = false;

    /// <summary>菜单标题随番茄相位切换（由 App 每秒刷新）。</summary>
    public void UpdatePomodoroMenuHeader(string header) => PetPomodoroMenuItem.Header = header;

    /// <summary>菜单显隐项随状态切换。</summary>
    public void UpdatePetVisibilityMenuHeader(bool visible) =>
        PetVisibilityMenuItem.Header = visible ? "隐藏宠物" : "显示宠物";

    private void OnPetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        _pressPosition = e.GetPosition(this);
        _pressTicks = Environment.TickCount64;
        _dragging = false;
    }

    // 超过阈值才进入拖动：原地按住不会触发 DragMove，从而保住单击语义
    private void OnPetMouseMove(object sender, MouseEventArgs e)
    {
        if (_clickThrough || _dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressPosition;
        if (Math.Abs(delta.X) <= ClickMoveTolerancePixels && Math.Abs(delta.Y) <= ClickMoveTolerancePixels)
        {
            return;
        }

        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 按下状态在事件排队期间丢失时 DragMove 会抛；按普通点击处理，不击穿 UI
            _dragging = false;
        }
    }

    private void OnPetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            return;
        }

        if (Environment.TickCount64 - _pressTicks > ClickMaxDurationMilliseconds)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressPosition;
        if (Math.Abs(delta.X) > ClickMoveTolerancePixels || Math.Abs(delta.Y) > ClickMoveTolerancePixels)
        {
            return;
        }

        PetMenu.PlacementTarget = PetRoot;
        PetMenu.Placement = PlacementMode.MousePoint;
        PetMenu.IsOpen = true;
    }

    private void OnMenuTogglePomodoro(object sender, RoutedEventArgs e) =>
        TogglePomodoroRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuDailyReport(object sender, RoutedEventArgs e) =>
        DailyReportRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuWeeklyReport(object sender, RoutedEventArgs e) =>
        WeeklyReportRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuHistory(object sender, RoutedEventArgs e) =>
        HistoryRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuSettings(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuSetCurrentCategory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag }
            && Enum.TryParse<RuleCategory>(tag, out var category))
        {
            SetCurrentCategoryRequested?.Invoke(this, category);
        }
    }

    private void OnMenuPickWindow(object sender, RoutedEventArgs e) =>
        WindowPickerRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuTogglePetVisibility(object sender, RoutedEventArgs e) =>
        HidePetRequested?.Invoke(this, EventArgs.Empty);

    private void OnMenuExit(object sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>鼠标穿透开关：开启后宠物窗不拦截鼠标消息（也不可拖动、不可弹菜单）。</summary>
    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        if (new System.Windows.Interop.WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;   // 句柄未创建（启动早期调用）：OnSourceInitialized 时补应用
        }

        NativeMethods.SetClickThrough(this, enabled);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_clickThrough)
        {
            NativeMethods.SetClickThrough(this, true);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((Storyboard)FindResource("BreathingStoryboard")).Begin(PetBreathHost);
        ApplyState(_viewModel.PostureState);
        ApplyState(_viewModel.MoodState);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PetViewModel.PostureState))
        {
            ApplyState(_viewModel.PostureState);
        }
        else if (e.PropertyName == nameof(PetViewModel.MoodState))
        {
            ApplyState(_viewModel.MoodState);
        }
    }

    // C# 只负责“进哪个状态”：全部动画在 XAML 的 VSM/资源里
    private void ApplyState(string stateName) =>
        VisualStateManager.GoToElementState(PetRoot, stateName, true);

    // 开发期调试热键（冒烟验收用）：F6 模拟 Focused，F7 模拟 Distracted，
    // F8 模拟"有效查资料"，F9 模拟"在逃避"。仅在定义了 DEBUG_HOTKEYS 时编译。
#if DEBUG_HOTKEYS
    private void OnDevKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F6:
                _viewModel.DevNavigate(FocusState.Focused);
                break;
            case Key.F7:
                _viewModel.DevNavigate(FocusState.Distracted);
                break;
            case Key.F8:
                _viewModel.MarkValidCommand.Execute(null);
                break;
            case Key.F9:
                _viewModel.MarkEscapeCommand.Execute(null);
                break;
        }
    }
#endif
}
