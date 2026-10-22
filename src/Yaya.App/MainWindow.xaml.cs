using System.Windows;
using System.Windows.Controls;
using Yaya.App.ViewModels;

namespace Yaya.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Loaded 时把已保存的 api_key 填入密码框（不触发回调保存循环的值相等，无害）。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            LlmApiKeyBox.Password = viewModel.CurrentApiKey;
        }
    }

    // PasswordBox 不支持绑定：密码变化经 VM.UpdateApiKey 流转（只进内存与本地 settings.json，不进日志）
    private void OnLlmApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateApiKey(box.Password);
        }
    }
}
