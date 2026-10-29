using System.Windows;

namespace Yaya.App;

/// <summary>
/// 首次运行说明页（模态）：复用设置页同一份数据用途文本，讲清「采什么/不采什么/数据在哪/怎么清空」。
/// 关闭即视为已读（App 写 HasCompletedOnboarding）；「开始第一次专注」只请求启动番茄，不直接操作服务。
/// </summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow(string dataUsageText)
    {
        InitializeComponent();
        UsageText.Text = dataUsageText;
    }

    /// <summary>用户是否点了「开始第一次专注」。</summary>
    public bool StartFocusRequested { get; private set; }

    private void OnStartFocusFirst(object sender, RoutedEventArgs e)
    {
        StartFocusRequested = true;
        Close();
    }

    private void OnBrowseFirst(object sender, RoutedEventArgs e) => Close();
}
