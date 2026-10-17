using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Yaya.App.Services;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;

namespace Yaya.App;

public partial class PetWindow : Window
{
    private readonly PetViewModel _viewModel;
    private bool _clickThrough;

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
        // 拖动只由宠物区（底部 200x200）触发；鼠标穿透开启时不可拖
        PetRoot.MouseLeftButtonDown += (_, _) =>
        {
            if (!_clickThrough)
            {
                DragMove();
            }
        };
    }

    /// <summary>鼠标穿透开关：开启后宠物窗不拦截鼠标消息（也不可拖动）。</summary>
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

    // 开发期调试热键（冒烟验收用，TODO(app): 发布前移除）：F6 模拟 Focused，F7 模拟 Distracted
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
}
