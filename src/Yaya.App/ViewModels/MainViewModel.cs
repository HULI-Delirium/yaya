using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>
/// 主窗体视图模型。由 App 注入两枚纠正回调，自身不碰数据库与状态机。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Action<string> _onMarkValid;
    private readonly Action<string> _onMarkEscape;

    public MainViewModel(Action<string> onMarkValid, Action<string> onMarkEscape)
    {
        _onMarkValid = onMarkValid;
        _onMarkEscape = onMarkEscape;
    }

    /// <summary>当前陪伴状态一句话，由感知循环回写。</summary>
    [ObservableProperty]
    private string _statusText = "芽芽正在观察……";

    /// <summary>用户纠正：刚才算有效学习。</summary>
    [RelayCommand]
    private void MarkValid() => _onMarkValid("主窗体按钮：刚才算有效");

    /// <summary>用户标记逃避：进入 2 分钟微重启。</summary>
    [RelayCommand]
    private void MarkEscape() => _onMarkEscape("主窗体按钮：逃避了");
}
