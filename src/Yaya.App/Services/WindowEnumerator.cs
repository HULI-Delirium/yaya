using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Yaya.Core.Monitoring;

namespace Yaya.App.Services;

/// <summary>窗口选择器的一行：进程名 + 标题 + 是否浏览器宿主。</summary>
public sealed record WindowCandidate(IntPtr Handle, string ProcessName, string Title, bool IsBrowser)
{
    /// <summary>列表显示用（截断，避免超长标题撑破 UI）。</summary>
    public string DisplayTitle => Title.Length > 120 ? Title[..120] + "…" : Title;
}

/// <summary>
/// 用户主动打开的「选择窗口」一次性枚举（App 层，仅内存）。
/// 红线：只在用户点击「选择窗口…」时枚举一次可见顶层窗口；标题仅用于本次选择界面，
/// 不写日志、不落库；规则只保存用户确认的进程名/关键词。绝不后台持续枚举。
/// </summary>
public static class WindowEnumerator
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);

    /// <summary>枚举可见、有标题、非芽芽自己的顶层窗口；失败/无权限的窗口静默跳过。</summary>
    public static IReadOnlyList<WindowCandidate> Enumerate()
    {
        var selfProcessName = Process.GetCurrentProcess().ProcessName;
        var processNames = new Dictionary<uint, string>();
        var windows = new List<WindowCandidate>();

        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return true;
                }

                var title = new StringBuilder(length + 1);
                _ = GetWindowText(hWnd, title, title.Capacity);
                var text = title.ToString().Trim();
                if (text.Length == 0)
                {
                    return true;
                }

                _ = GetWindowThreadProcessId(hWnd, out var processId);
                if (processId == 0)
                {
                    return true;
                }

                var processName = GetProcessName(processId, processNames);
                if (string.IsNullOrWhiteSpace(processName)
                    || string.Equals(processName, selfProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 工具窗口（浮动面板等）不进入选择列表
                var exStyle = GetWindowLongPtr(hWnd, GwlExStyle);
                if ((exStyle & WsExToolWindow) != 0)
                {
                    return true;
                }

                windows.Add(new WindowCandidate(
                    hWnd, processName, text, AppCatalog.IsBrowserHost(processName)));
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 枚举失败返回已收集的部分；绝不让选择器崩掉主程序
        }

        return windows
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetProcessName(uint processId, Dictionary<uint, string> cache)
    {
        if (cache.TryGetValue(processId, out var cached))
        {
            return cached;
        }

        string name;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            name = string.Empty;
        }

        cache[processId] = name;
        return name;
    }
}
