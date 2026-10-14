using System.Runtime.InteropServices;
using System.Text;

namespace Yaya.App.Services;

/// <summary>
/// 采集边界（隐私红线）：只读取前台窗口的“标题文本”和系统级键鼠空闲时长。
/// 不截屏、不读摄像头/麦克风、不枚举进程路径与 URL。
/// </summary>
internal static class NativeMethods
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // 由前台窗口句柄反查所属进程 ID（只读元数据，配合 ProcessName 用于类别映射）
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    /// <summary>取前台窗口标题；取不到时返回空串（交由分类器判为 Unknown）。</summary>
    public static string GetForegroundWindowTitle()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    /// <summary>取前台窗口所属进程名；句柄失效或进程刚好退出等任何异常都返回空串（判 Unknown，不崩）。</summary>
    public static string GetForegroundProcessName()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            // Process 对象必须 Dispose；进程在两步调用之间退出时此处会抛异常，按空串降级
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>距上次键鼠输入的空闲秒数。用 uint 换算避免 24.9 天回绕误差。</summary>
    public static double GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        var idleMs = (uint)Environment.TickCount - info.dwTime;
        return idleMs / 1000.0;
    }
}
