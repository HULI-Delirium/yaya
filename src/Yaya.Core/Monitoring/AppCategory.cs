namespace Yaya.Core.Monitoring;

/// <summary>
/// 由前台窗口标题推断出的应用类别。
/// 隐私红线：输入只来自窗口标题文本，不来自进程路径、URL 或屏幕内容。
/// </summary>
public enum AppCategory
{
    Study = 0,
    Work = 1,
    Entertainment = 2,
    Social = 3,
    Unknown = 4,
}
