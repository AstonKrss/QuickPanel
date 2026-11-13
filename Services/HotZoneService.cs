using System.Runtime.InteropServices;
using System.Windows;
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int OutsideHideMs = 300; // F31 v0.9：默认 300ms（HideDelayMs 可调）

    public PanelWindow? Panel { get; set; }
    public DockBarWindow? Dock { get; set; }
    public Func<int> HotZoneMs { get; set; } = () => 300;

    /// <summary>F35 v0.6：可开关的全屏自动隐藏（设置 DockAutoHide）。</summary>
    public Func<bool> FullscreenHideEnabled { get; set; } = () => true;

    /// <summary>F31 v0.8：离开面板+Dock 后自动收回延迟（ms）。</summary>
    public Func<int> HideDelayMs { get; set; } = () => 600;

    /// <summary>v1.4：Dock 智能自动隐藏——平时隐藏，鼠标碰底部区域浮现。</summary>
    public Func<bool> DesktopOnly { get; set; } = () => true;

    private bool _hiddenForDesktop;
    private DateTime _bottomHoverSince = DateTime.MinValue;
    /// <summary>v1.7：面板显示后光标是否进入过面板 —— 未进入过则不启动自动收回计时。</summary>
    private bool _cursorEnteredPanel;

    /// <summary>底部热区高度（px）：鼠标进入屏幕底部这个范围内，Dock 浮现。</summary>
    private const int BottomRevealBand = 60;

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
            // v1.22：Dock 常驻时跳过这一整段（Dock 不隐藏，但仍允许面板收起）
            var dockAlways = DockAlwaysVisible();
            if (!dockAlways && IsForegroundFullscreen() && FullscreenHideEnabled())
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

            // v1.22：常驻模式下若之前被全屏隐藏过，立即恢复
            if (dockAlways && _dockHidden)
            {
                dock.RestoreFromFullscreen();
                _dockHidden = false;
            }

            if (_dockHidden)
            {
                dock.RestoreFromFullscreen();
                _dockHidden = false;
            }

            if (!Win32.GetCursorPos(out var cursor)) return;

            // v1.4：智能自动隐藏 —— 非桌面时隐藏 Dock；鼠标碰底部区域则浮现
            // v1.22：Dock 常驻时完全跳过（Dock 一直显示）
            if (!dockAlways && DesktopOnly())
            {
                var onDesktop = IsDesktopForeground();
                if (!onDesktop)
                {
                    // 光标是否在屏幕底部热区（不依赖窗口命中，Dock 隐藏时也能触发）
                    var wa = SystemParameters.WorkArea;
                    var nearBottom = cursor.Y >= wa.Bottom - BottomRevealBand
                                     && cursor.X >= wa.Left && cursor.X <= wa.Right;

                    if (nearBottom)
                    {
                        if (_bottomHoverSince == DateTime.MinValue) _bottomHoverSince = DateTime.Now;
                        // 停 120ms 才浮现，避免鼠标扫过底部时闪一下
                        if ((DateTime.Now - _bottomHoverSince).TotalMilliseconds >= 120 && _hiddenForDesktop)
                        {
                            _hiddenForDesktop = false;
                            dock.RestoreFromDesktop();
                        }
                    }
                    else
                    {
                        _bottomHoverSince = DateTime.MinValue;
                        if (!_hiddenForDesktop)
                        {
                            _hiddenForDesktop = true;
                            if (panel.IsVisible) panel.HidePanel();
                            dock.CollapseForDesktop();
                        }
                    }

                    if (!dock.IsVisible)
                    {
                        ResetHover();
                        ResetOutside();
                        return;
                    }
                }
                else if (_hiddenForDesktop)
                {
                    // 回到桌面 → 立即恢复
                    _hiddenForDesktop = false;
                    _bottomHoverSince = DateTime.MinValue;
                    dock.RestoreFromDesktop();
                }
            }
            else if (_hiddenForDesktop)
            {
                _hiddenForDesktop = false;
                dock.RestoreFromDesktop();
            }

            var dockRect = dock.GetScreenRect();
            // v0.7：真实命中检测——几何矩形内 且 光标确实落在 Dock 窗口上（被其他窗口覆盖时不算悬停）
            var overDock = InRect(cursor, dockRect, inflate: 4) && CursorHits(cursor, dock.GetHwnd());

            // F31：悬停 Dock ≥ HotZoneMs 唤出面板（v0.8：仅分组按钮上触发，悬停图钉不弹窗）
            // v1.7：计时只在"光标确实在分组按钮上"时累加，避免停在图钉/齿轮上再滑过去就秒开
            if (overDock && !panel.IsVisible && dock.IsOverGroupButton(cursor))
            {
                if (_dockHoverSince == DateTime.MinValue) _dockHoverSince = DateTime.Now;
                if ((DateTime.Now - _dockHoverSince).TotalMilliseconds >= HotZoneMs())
                {
                    dock.SelectGroupUnderCursor(); // F21 v0.5：悬停哪个分组按钮就定位哪个分组
                    panel.ShowPanel();
                }
                ResetOutside();
                return;
            }
            if (!overDock || !dock.IsOverGroupButton(cursor))
                ResetHover();

            ResetHover();

            if (!panel.IsVisible) return;

            // v1.20：把"面板与 Dock 之间的间隙"也算安全区 ——
            // 否则从 Dock 移向面板的那一瞬间光标落在两者之间，面板会立刻消失。
            var panelRect = panel.GetScreenRect();
            var dockRectForBridge = dock.GetScreenRect();
            var inBridge = IsInBridgeZone(cursor, dockRectForBridge, panelRect);

            var outside = !overDock && !InRect(cursor, panelRect) && !inBridge;

            // v1.7：热键/托盘唤出时鼠标从未进入面板 —— 不能因为"鼠标在外面"就收回
            // （此前 Alt+Q 打开后 300ms 立即消失，用户根本来不及操作）
            if (!outside) _cursorEnteredPanel = true;
            if (!_cursorEnteredPanel)
            {
                ResetOutside();
                return;
            }

            if (outside)
            {
                // v1.20：常驻模式 —— 不做任何自动收回
                if (StayOpen())
                {
                    ResetOutside();
                    return;
                }

                // v1.35：用户正按着左键（多半在从桌面/资源管理器拖文件过来）→ 不收回面板，
                // 否则刚拖到一半面板就消失，文件无处可放。
                if (Win32.IsLeftButtonDown())
                {
                    ResetOutside();
                    return;
                }

                if (_outsideSince == DateTime.MinValue) _outsideSince = DateTime.Now;
                var delay = HideDelayMs();
                if (delay <= 0 || (DateTime.Now - _outsideSince).TotalMilliseconds >= delay)
                {
                    panel.HidePanel();
                    _cursorEnteredPanel = false; // v1.7：下次唤出重新判定
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

    // v1.37：全屏检测结果缓存 —— 之前每 50ms 调一次 SHQueryUserNotificationState（COM 调用），
    // 常驻时是隐性 CPU 开销。现在最多每 400ms 采样一次，且前台窗口没变就直接复用。
    private bool _fullscreenCached;
    private IntPtr _fullscreenHwnd = IntPtr.Zero;
    private DateTime _fullscreenCheckedAt = DateTime.MinValue;

    private bool IsForegroundFullscreen()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // 同一窗口 400ms 内复用结果
        if (hwnd == _fullscreenHwnd && (DateTime.Now - _fullscreenCheckedAt).TotalMilliseconds < 400)
            return _fullscreenCached;

        _fullscreenHwnd = hwnd;
        _fullscreenCheckedAt = DateTime.Now;
        _fullscreenCached = QueryFullscreen(hwnd);
        return _fullscreenCached;
    }

    private bool QueryFullscreen(IntPtr hwnd)
    {

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

    /// <summary>v1.20：面板常驻 —— 为 true 时不做任何自动收回。</summary>
    public Func<bool> StayOpen { get; set; } = () => false;

    /// <summary>v1.22：Dock 常驻 —— 为 true 时 Dock 永不自动隐藏（无视全屏隐藏与仅桌面显示）。</summary>
    public Func<bool> DockAlwaysVisible { get; set; } = () => false;

    /// <summary>v1.20：Dock 与面板之间的过渡带。</summary>
    /// 光标处于两者之间的垂直间隙、且水平落在合并范围内 → 视为"正在移动中"，不收回。
    /// </summary>
    private static bool IsInBridgeZone(Win32.POINT p, Win32.RECT dockRect, Win32.RECT panelRect)
    {
        try
        {
            int gapTop, gapBottom;
            if (panelRect.Bottom <= dockRect.Top)          // 面板在上方
            {
                gapTop = panelRect.Bottom;
                gapBottom = dockRect.Top;
            }
            else if (dockRect.Bottom <= panelRect.Top)      // 面板在下方
            {
                gapTop = dockRect.Bottom;
                gapBottom = panelRect.Top;
            }
            else return false;

            if (gapBottom - gapTop > 40) return false;
            if (p.Y < gapTop - 2 || p.Y > gapBottom + 2) return false;

            var left = Math.Min(dockRect.Left, panelRect.Left);
            var right = Math.Max(dockRect.Right, panelRect.Right);
            return p.X >= left && p.X <= right;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>v0.7：光标是否真实命中目标窗口（GA_ROOT 比对，屏蔽其他窗口覆盖）。</summary>
    private static bool CursorHits(Win32.POINT cursor, IntPtr targetHwnd)
    {
        var hit = Win32.WindowFromPoint(cursor);
        if (hit == IntPtr.Zero) return false;
        var root = Win32.GetAncestor(hit, Win32.GA_ROOT);
        return root == targetHwnd;
    }

    /// <summary>
    /// v1.4：前台是否处于"桌面"状态。
    /// 桌面窗口类名：Progman（Win10/11）、WorkerW（壁纸层）、Shell_TrayWnd（任务栏）、
    /// 以及 QuickPanel 自己的窗口（Dock/面板/设置）。
    /// </summary>
    private bool IsDesktopForeground()
    {
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return true; // 无前台窗口时按桌面处理

        var cls = Win32.ClassNameOf(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland")
            return true;

        // 自己的窗口（Dock / 面板 / 设置 / 输入框）也算桌面态，否则悬停 Dock 会把自己藏掉
        var self = System.Diagnostics.Process.GetCurrentProcess().Id;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == self;
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
