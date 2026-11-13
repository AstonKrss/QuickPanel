using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace QuickPanel.Services;

/// <summary>
/// v1.36：桌面留白 —— 隐藏桌面图标但保留壁纸。
///
/// 与 v1.30 的关键区别：以前隐藏 SHELLDLL_DefView，而 DefView **同时负责绘制壁纸**，
/// 所以桌面会整片变黑。现在改用 Windows 自己那个开关（桌面右键 → 查看 → 显示桌面图标），
/// 它由 SysListView32（图标层）的显示状态决定，壁纸不受影响。
///
/// 实现：向 Progman 的 DefView 发 WM_COMMAND(0x7402) —— 这是 Explorer 内部
/// "切换桌面图标"的命令；同时把结果同步到 HideIcons 注册表，重启 Explorer 后保持一致。
/// </summary>
public static class DesktopIcons
{
    private const uint WM_COMMAND = 0x0111;
    private const int CMD_TOGGLE_DESKTOP_ICONS = 0x7402;
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "HideIcons";

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    /// <summary>当前是否隐藏（以注册表为准，Explorer 会同步写入）。</summary>
    public static bool IsHidden()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            if (key?.GetValue(ValueName) is { } raw)
                return Convert.ToInt32(raw) != 0;
        }
        catch { /* 读注册表失败时按"显示"处理 */ }
        return false;
    }

    /// <summary>设置桌面图标显示/隐藏。返回是否达成目标状态。</summary>
    public static bool SetHidden(bool hidden)
    {
        if (IsHidden() == hidden) return true;

        if (!TrySendToggle())
        {
            // 命令没送到（Explorer 结构异常）→ 直接写注册表兜底
            WriteRegistry(hidden);
            return IsHidden() == hidden;
        }

        // Explorer 异步处理：注册表与图标层都可能滞后，最多等 ~3s
        for (var i = 0; i < 30; i++)
        {
            Thread.Sleep(100);
            if (IsHidden() == hidden && IconLayerVisibilityMatches(hidden)) return true;
        }

        // 已收到命令但重绘慢：以注册表为准（视觉效果随后即到）
        return IsHidden() == hidden;
    }

    public static bool Toggle() => SetHidden(!IsHidden());

    /// <summary>向桌面视图发切换命令。优先 Progman，其次任意 WorkerW 下的 DefView。</summary>
    private static bool TrySendToggle()
    {
        foreach (var view in EnumerateDesktopViews())
        {
            try
            {
                var ok = SendMessageTimeout(view, WM_COMMAND, new IntPtr(CMD_TOGGLE_DESKTOP_ICONS),
                    IntPtr.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, 2000, out _);
                if (ok != IntPtr.Zero)
                {
                    // 命令已受理；强制重绘一次，避免要等下次鼠标移动才刷新
                    InvalidateRect(view, IntPtr.Zero, true);
                    UpdateWindow(view);
                    return true;
                }
            }
            catch { /* 换下一个目标 */ }
        }
        return false;
    }

    private static List<IntPtr> EnumerateDesktopViews()
    {
        var views = new List<IntPtr>();

        // 1) Progman\SHELLDLL_DefView（常规桌面）
        var progman = FindWindow("Progman", null);
        var direct = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (direct != IntPtr.Zero) views.Add(direct);

        // 2) WorkerW\SHELLDLL_DefView（壁纸轮播等场景会换父窗口）
        EnumWindows((hWnd, _) =>
        {
            var child = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (child != IntPtr.Zero && !views.Contains(child)) views.Add(child);
            return true;
        }, IntPtr.Zero);

        return views;
    }

    /// <summary>
    /// 图标层（SysListView32）可见性是否与期望一致。
    /// 注意：只用于确认，绝不用 ShowWindow 去隐藏它 —— 那会连壁纸一起黑。
    /// </summary>
    private static bool IconLayerVisibilityMatches(bool hidden)
    {
        foreach (var view in EnumerateDesktopViews())
        {
            var list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
            if (list == IntPtr.Zero) continue;
            return IsWindowVisible(list) != hidden;
        }
        return true; // 拿不到图标层时不做判定
    }

    private static void WriteRegistry(bool hidden)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AdvancedKey);
            key?.SetValue(ValueName, hidden ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 权限受限时忽略 */ }
    }
}
