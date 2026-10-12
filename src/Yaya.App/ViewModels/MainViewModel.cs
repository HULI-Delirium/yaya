using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yaya.Core.Monitoring;

namespace Yaya.App.ViewModels;

/// <summary>
/// 主窗体视图模型。由 App 注入“纠正上报”回调，自身不碰数据库与状态机。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Action<FocusState, string> _reportCorrection;

    public MainViewModel(Action<FocusState, string> reportCorrection)
    {
        _reportCorrection = reportCorrection;
    }

    /// <summary>当前陪伴状态一句话，由感知循环回写。</summary>
    [ObservableProperty]
    private string _statusText = "芽芽正在观察……";

    /// <summary>用户纠正：刚才其实在学习。</summary>
    [RelayCommand]
    private void CorrectToFocused() =>
        _reportCorrection(FocusState.Focused, "用户说明：刚才其实在学习");

    /// <summary>用户纠正：刚才其实在游离。</summary>
    [RelayCommand]
    private void CorrectToDistracted() =>
        _reportCorrection(FocusState.Distracted, "用户说明：刚才其实在游离");
}
