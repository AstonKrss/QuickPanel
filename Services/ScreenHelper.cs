using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickPanel.Services;

/// <summary>
/// v1.16：多显示器支持 —— 枚举显示器、取指定窗口/坐标所在屏的工作区。
/// 所有坐标均为 DIP（与 WPF Window.Left/Top 一致）。
/// </summary>
public static class ScreenHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    /// <summary>取窗口所在显示器的工作区（DIP）。失败回退主屏 WorkArea。</summary>
    public static Rect GetWorkAreaForWindow(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;

            var hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hmon == IntPtr.Zero) return SystemParameters.WorkArea;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hmon, ref mi)) return SystemParameters.WorkArea;

            // 物理像素 → DIP
            var scale = GetScaleForWindow(hwnd);
            return new Rect(
                mi.rcWork.Left / scale,
                mi.rcWork.Top / scale,
                (mi.rcWork.Right - mi.rcWork.Left) / scale,
                (mi.rcWork.Bottom - mi.rcWork.Top) / scale);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    /// <summary>取鼠标光标所在显示器的工作区（DIP，虚拟屏坐标）。</summary>
    public static Rect GetWorkAreaAtCursor()
    {
        try
        {
            if (!Win32.GetCursorPos(out var cur)) return SystemParameters.WorkArea;
            var pt = new POINT { X = cur.X, Y = cur.Y };
            var hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hmon == IntPtr.Zero) return SystemParameters.WorkArea;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hmon, ref mi)) return SystemParameters.WorkArea;

            // 用主屏 DPI 近似（多屏不同 DPI 时会有偏差，但比完全用主屏准）
            var scale = GetPrimaryScale();
            return new Rect(
                mi.rcWork.Left / scale,
                mi.rcWork.Top / scale,
                (mi.rcWork.Right - mi.rcWork.Left) / scale,
                (mi.rcWork.Bottom - mi.rcWork.Top) / scale);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    /// <summary>窗口的 DPI 缩放（1.0 / 1.25 / 1.5 …）。</summary>
    public static double GetScaleForWindow(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : GetPrimaryScale();
        }
        catch
        {
            return GetPrimaryScale();
        }
    }

    private static double GetPrimaryScale()
    {
        try
        {
            var src = System.Windows.Media.VisualTreeHelper.GetDpi(
                new System.Windows.Media.DrawingVisual());
            return src.DpiScaleX;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>把窗口夹到指定工作区内（防止跑到屏幕外）。</summary>
    public static void ClampWindow(Window window, Rect work)
    {
        if (double.IsNaN(window.Left) || double.IsNaN(window.Top)) return;

        // 若窗口完全在工作区外（例如显示器已断开），拉回中心
        var w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        var outside = window.Left + w < work.Left || window.Left > work.Right
                      || window.Top + h < work.Top || window.Top > work.Bottom;
        if (outside)
        {
            window.Left = work.Left + (work.Width - w) / 2;
            window.Top = work.Top + (work.Height - h) / 2;
            return;
        }

        window.Left = Math.Max(work.Left, Math.Min(window.Left, work.Right - w));
        window.Top = Math.Max(work.Top, Math.Min(window.Top, work.Bottom - h));
    }
}
