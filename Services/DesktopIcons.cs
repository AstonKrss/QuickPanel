using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace QuickPanel.Services;

/// <summary>
/// v1.30：桌面图标显示/隐藏（"桌面留白"）。
/// 只切换 Explorer 桌面视图的可见性，快捷方式仍在桌面文件夹里，
/// 资源管理器里照常可见可打开（Fences 的"隐藏桌面"也是这个效果）。
///
/// 注：网上常见的 WM_COMMAND 0x7402 在 Win11 24H2 上已无效（实测 Progman /
/// SHELLDLL_DefView / SysListView32 三个目标都不响应），改用 ShowWindow 直接
/// 隐藏 DefView，并同步 HideIcons 注册表值，保证 Explorer 重启后状态一致。
/// </summary>
public static class DesktopIcons
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static bool IsHidden()
    {
        var view = FindDesktopView();
        return view != IntPtr.Zero && !IsWindowVisible(view);
    }

    /// <summary>设置桌面图标显示/隐藏。返回是否达成目标状态。</summary>
    public static bool SetHidden(bool hidden)
    {
        var view = FindDesktopView();
        if (view == IntPtr.Zero) return false;
        if (IsWindowVisible(view) == !hidden) return true;

        ShowWindow(view, hidden ? SW_HIDE : SW_SHOW);

        // 同步注册表：Explorer 重启后仍保持同一状态
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AdvancedKey);
            key?.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 注册表写失败不影响本次会话 */ }

        // 等窗口状态生效
        for (var i = 0; i < 10; i++)
        {
            if (IsWindowVisible(view) == !hidden) return true;
            Thread.Sleep(80);
        }
        return IsWindowVisible(view) == !hidden;
    }

    public static bool Toggle() => SetHidden(!IsHidden());

    private static IntPtr FindDesktopView()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        return FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
    }
}
