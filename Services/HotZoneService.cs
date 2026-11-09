using System.Runtime.InteropServices;
using System.Windows.Threading;
using QuickPanel.Windows;

namespace QuickPanel.Services;

/// <summary>热区呼出 / 离开收回 / 全屏检测（PRD F31/F35：200ms DispatcherTimer 轮询）。</summary>
public class HotZoneService
{
    private enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);

    private const int OutsideHideMs = 300; // F31 v0.9：默认 300ms（HideDelayMs 可调）

    public PanelWindow? Panel { get; set; }
    public DockBarWindow? Dock { get; set; }
    public Func<int> HotZoneMs { get; set; } = () => 300;

    /// <summary>F35 v0.6：可开关的全屏自动隐藏（设置 DockAutoHide）。</summary>
    public Func<bool> FullscreenHideEnabled { get; set; } = () => true;

    /// <summary>F31 v0.8：离开面板+Dock 后自动收回延迟（ms）。</summary>
    public Func<int> HideDelayMs { get; set; } = () => 600;

    private readonly DispatcherTimer _timer;
    private DateTime _dockHoverSince = DateTime.MinValue;
    private DateTime _outsideSince = DateTime.MinValue;
    private bool _dockHidden;

    public HotZoneService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50), // v1.0：50ms 轮询（收回最跟手，单次 <1μs）
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Tick()
    {
        try
        {
            var panel = Panel;
            var dock = Dock;
            if (panel is null || dock is null) return;

            // F35：前台全屏（D3D 独占/演示/软全屏）→ Dock+面板自动收起；退出全屏恢复 Dock
            if (IsForegroundFullscreen() && FullscreenHideEnabled())
            {
                if (!_dockHidden && dock.IsVisible)
                {
                    dock.CollapseForFullscreen();
                    _dockHidden = true;
                }
                if (panel.IsVisible) panel.HidePanel();
                ResetHover();
                ResetOutside();
                return;
            }

            if (_dockHidden)
            {
                dock.RestoreFromFullscreen();
                _dockHidden = false;
            }

            if (!Win32.GetCursorPos(out var cursor)) return;
            var dockRect = dock.GetScreenRect();
            // v0.7：真实命中检测——几何矩形内 且 光标确实落在 Dock 窗口上（被其他窗口覆盖时不算悬停）
            var overDock = InRect(cursor, dockRect, inflate: 4) && CursorHits(cursor, dock.GetHwnd());

            // F31：悬停 Dock ≥ HotZoneMs 唤出面板（v0.8：仅分组按钮上触发，悬停图钉不弹窗）
            if (overDock && !panel.IsVisible)
            {
                if (_dockHoverSince == DateTime.MinValue) _dockHoverSince = DateTime.Now;
                if ((DateTime.Now - _dockHoverSince).TotalMilliseconds >= HotZoneMs()
                    && dock.IsOverGroupButton(cursor))
                {
                    dock.SelectGroupUnderCursor(); // F21 v0.5：悬停哪个分组按钮就定位哪个分组
                    panel.ShowPanel();
                }
                ResetOutside();
                return;
            }

            ResetHover();

            if (!panel.IsVisible) return;

            var outside = !overDock && !InRect(cursor, panel.GetScreenRect());
            if (outside)
            {
                if (_outsideSince == DateTime.MinValue) _outsideSince = DateTime.Now;
                var delay = HideDelayMs();
                if (delay <= 0 || (DateTime.Now - _outsideSince).TotalMilliseconds >= delay)
                {
                    panel.HidePanel();
                    ResetOutside();
                }
            }
            else
            {
                ResetOutside();
            }
        }
        catch
        {
            // 轮询容错：任何单次失败不影响下一轮
        }
    }

    private bool IsForegroundFullscreen()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // F35 v0.5：改用系统通知状态 API 判定（无边框最大化的普通窗口不会再误判）
        // D3D 独占全屏 / 演示模式 → 一定全屏；BUSY（浏览器 F11 等软全屏）→ 结合矩形覆盖确认
        var hr = SHQueryUserNotificationState(out var state);
        if (hr != 0)
            return RectCoversMonitor(hwnd); // API 失败时回退矩形判定

        return state switch
        {
            QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => true,
            QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => true,
            QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY => RectCoversMonitor(hwnd),
            _ => false,
        };
    }

    /// <summary>v0.7：光标是否真实命中目标窗口（GA_ROOT 比对，屏蔽其他窗口覆盖）。</summary>
    private static bool CursorHits(Win32.POINT cursor, IntPtr targetHwnd)
    {
        var hit = Win32.WindowFromPoint(cursor);
        if (hit == IntPtr.Zero) return false;
        var root = Win32.GetAncestor(hit, Win32.GA_ROOT);
        return root == targetHwnd;
    }

    private bool RectCoversMonitor(IntPtr hwnd)
    {
        if (!Win32.GetWindowRect(hwnd, out var rect)) return false;
        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref mi)) return false;
        return rect.Left <= mi.rcMonitor.Left && rect.Top <= mi.rcMonitor.Top
            && rect.Right >= mi.rcMonitor.Right && rect.Bottom >= mi.rcMonitor.Bottom;
    }

    private static bool InRect(Win32.POINT p, Win32.RECT r, int inflate = 0)
        => p.X >= r.Left - inflate && p.X <= r.Right + inflate
        && p.Y >= r.Top - inflate && p.Y <= r.Bottom + inflate;

    private void ResetHover() => _dockHoverSince = DateTime.MinValue;
    private void ResetOutside() => _outsideSince = DateTime.MinValue;
}
