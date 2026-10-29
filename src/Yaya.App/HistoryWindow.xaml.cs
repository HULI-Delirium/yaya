using System.Windows;

namespace Yaya.App;

/// <summary>「专注记录」窗：只读展示近 14 天，非模态、可最小化；数据由 App 构建后注入。</summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
    }
}
