using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using QuickPanel.Services;
using QuickPanel.ViewModels;
using QuickPanel.Windows;
using WinForms = System.Windows.Forms;
using SD = System.Drawing;

namespace QuickPanel;

public partial class App : Application
{
    private SingleInstanceGuard? _guard;
    private ConfigStore? _store;
    private MainViewModel? _vm;
    private PanelWindow? _panel;
    private DockBarWindow? _dock;
    private HotkeyService? _hotkeys;
    private HotZoneService? _hotzone;
    private SettingsWindow? _settings;
    private DeskCardWindow? _deskClock;
    private DeskCardWindow? _deskMonitor;
    private WinForms.NotifyIcon? _tray;
    private SD.Icon? _trayIcon;
    private string _currentBackgroundMode = "acrylic";
    private bool _currentPlate = true;
    private bool _currentNearInvisible;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // §5.4：全局异常 → error.log，尽量存活
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject);

        // F33：单实例（Mutex + EventWaitHandle 双保险）
        _guard = new SingleInstanceGuard();
        _guard.Acquire();
        if (!_guard.IsFirstInstance)
        {
            SingleInstanceGuard.SignalExisting(); // 唤出既有实例
            Shutdown();
            return;
        }

        WinForms.Application.EnableVisualStyles();

        _store = new ConfigStore();
        try
        {
            _store.Load();
        }
        catch (IOException ex)
        {
            // v1.7：配置文件被占用/无权限 —— 不覆盖用户数据，提示后退出
            MessageBox.Show(
                $"无法读取配置文件（可能被其他程序占用）：\n{ConfigStore.ConfigPath}\n\n{ex.Message}\n\n请关闭占用程序后重试。",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        _vm = new MainViewModel(_store);
        // v1.23：首运时交给初始化向导播种，避免重复导入
        _vm.LoadFromConfig(seedOnFirstRun: !_store.FirstRun);
        _vm.SaveConfig(); // 首运播种 / 迁移后立即持久化（F3/F43/F44）

        Theme.Apply(_store.Config); // v0.5：外观颜色令牌
        _currentBackgroundMode = _store.Config.Settings.BackgroundMode;
        _currentPlate = _store.Config.Settings.DockIconPlate;
        _currentNearInvisible = Theme.NearInvisible;

        _panel = new PanelWindow(_vm);
        _dock = new DockBarWindow(_vm, ShowPanel, OpenSettings);
        _panel.SetDock(_dock);
        _panel.ApplyScale(); // v1.7：启动即应用保存的面板缩放（此前重启后失效）
        _panel.IsVisibleChanged += Panel_HiddenForTrim; // v1.32：面板收起即整理内存
        _dock.Opacity = _store.Config.Settings.Opacity;

        _hotkeys = new HotkeyService();
        _hotkeys.HotkeyPressed += TogglePanel;
        RegisterHotkeyWithFallback();

        var store = _store;
        _hotzone = new HotZoneService
        {
            Panel = _panel,
            Dock = _dock,
            HotZoneMs = () => store!.Config.Settings.HotZoneMs,
            FullscreenHideEnabled = () => store!.Config.Settings.DockAutoHide, // F35 v0.6 开关
            HideDelayMs = () => store!.Config.Settings.PanelHideMs,            // F31 v0.8 收回延迟
            StayOpen = () => store!.Config.Settings.PanelStayOpen,             // v1.21 面板常驻
            DockAlwaysVisible = () => store!.Config.Settings.DockAlwaysVisible, // v1.22 Dock 常驻
            DesktopOnly = () => store!.Config.Settings.DockDesktopOnly,        // v1.4 仅桌面显示
        };

        SetupTray();
        // F34 v0.6：分组变化时同步托盘快速入口
        _vm.Groups.CollectionChanged += (_, _) => Dispatcher.Invoke(RebuildTrayMenu);

        _dock.Show();
        MainWindow = _dock;
        CreateDeskCards(); // v1.36：桌面组件卡片
        _hotzone.Start();
        _guard.StartActivateListener(() => Dispatcher.Invoke(ShowPanel));


        // v1.20：预热面板 —— 启动时以 0 透明度显示一次再隐藏，
        // 让 WPF 提前完成首次布局/渲染/字体加载，消除第一次唤出的明显延迟。
        // 注意：直接 Show/Hide，不走 ShowPanel 的动画逻辑，避免与 Opacity 动画冲突。
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                if (_panel is null) return;
                var original = _panel.Opacity;
                _panel.Opacity = 0;
                _panel.Show();
                _panel.UpdateLayout();   // 强制完成一轮布局+渲染
                _panel.Hide();
                _panel.Opacity = original;
            }
            catch { /* 预热失败不影响正常使用 */ }
        }));

        // F42 v0.5 / v1.23：首次运行显示初始化向导（欢迎→扫描勾选→热键→完成）
        if (_store.FirstRun)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowOnboarding));

        // v1.32：内存整理 —— 启动完成一次，之后每 60s 检查（面板隐藏时才执行）
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var memTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            memTimer.Tick += (_, _) => TrimMemoryIfIdle();
            memTimer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _lastMemoryTrim = DateTime.MinValue; // 允许启动后立即整理一次
                TrimMemoryIfIdle();
            }));
        }));
    }

    /// <summary>v1.23：首次运行初始化向导。</summary>
    private void ShowOnboarding()
    {
        try
        {
            var wizard = new OnboardingWindow(_vm!, ApplyHotkey, ApplyAppearance);
            wizard.ShowDialog();

            // 向导结束后刷新界面（分组/钉选/热键可能变了）
            _dock?.ApplyLayout();
            _panel?.ApplyScale();
            RebuildTrayMenu();
        }
        catch (Exception ex)
        {
            LogError(ex);
            // 向导失败时退回设置窗口，保证用户能继续操作
            try { OpenSettings(); } catch { /* 忽略 */ }
        }
    }

    // ---- v1.32：常驻内存整理 ----

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    private DateTime _lastMemoryTrim = DateTime.MinValue;

    /// <summary>
    /// 常驻启动器 99% 时间在闲置：GC 压实 + 把工作集还给系统，
    /// 让任务管理器里的常驻内存回到真实占用。只在面板隐藏时执行（整理瞬间有 ~100ms 停顿）。
    /// </summary>
    private void TrimMemoryIfIdle()
    {
        if (_panel is { IsVisible: true }) return;
        if ((DateTime.Now - _lastMemoryTrim).TotalSeconds < 60) return;
        _lastMemoryTrim = DateTime.Now;
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            SetProcessWorkingSetSize(new IntPtr(-1), new IntPtr(-1), new IntPtr(-1));
        }
        catch { /* 整理失败无感，下个周期重试 */ }
    }

    private void Panel_HiddenForTrim(object? sender, DependencyPropertyChangedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _lastMemoryTrim = DateTime.MinValue;
            TrimMemoryIfIdle();
        }));

    protected override void OnExit(ExitEventArgs e)
    {

        try { _deskClock?.Close(); _deskMonitor?.Close(); } catch { }
        _tray?.Dispose();
        _hotzone?.Stop();
        _hotkeys?.Dispose();
        _guard?.Dispose();
        base.OnExit(e);
    }

    // ---------- 面板唤出 ----------

    private void ShowPanel() => _panel?.ShowPanel();

    private void TogglePanel()
    {
        if (_panel is null) return;
        if (_panel.IsVisible) _panel.HidePanel();
        else _panel.ShowPanel();
    }

    // ---------- 热键（F30：冲突自动回退并提示）----------

    private void RegisterHotkeyWithFallback(string? preferred = null)
    {
        var want = preferred ?? _store!.Config.Settings.Hotkey;
        foreach (var hotkey in new[] { want, "Ctrl+Alt+Q", "Ctrl+Alt+Shift+Q" }.Distinct())
        {
            if (!_hotkeys!.Register(hotkey, out var error)) continue;
            if (!string.Equals(hotkey, want, StringComparison.OrdinalIgnoreCase))
            {
                _store!.Config.Settings.Hotkey = hotkey;
                _vm!.SaveConfig();
                MessageBox.Show($"热键 {want} 注册失败（{error}），已自动回退为 {hotkey}。",
                    "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }
        MessageBox.Show("全局热键注册失败，可在设置中更换热键；仍可通过鼠标热区与托盘唤出面板。",
            "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ApplyHotkey(string hotkey) => RegisterHotkeyWithFallback(hotkey);

    private void ApplyOpacity(double value)
    {
        if (_dock != null) _dock.Opacity = value;
        if (_panel is { IsVisible: true }) _panel.Opacity = value;
    }

    // ---------- 外观（F50/F54 v0.5）----------

    private void ApplyAppearance()
    {
        if (_store is null) return;
        Theme.Apply(_store.Config);
        var mode = _store.Config.Settings.BackgroundMode;
        var invisible = Theme.NearInvisible;
        if (!string.Equals(mode, _currentBackgroundMode, StringComparison.OrdinalIgnoreCase)
            || invisible != _currentNearInvisible)
        {
            _currentBackgroundMode = mode;
            _currentNearInvisible = invisible;
            RecreateWindows(); // AllowsTransparency / 阴影形态必须显示前设定 → 重建
            return;
        }
        // v1.26：仅颜色变化时，让 Dock 重新应用文字色等令牌（不重建窗口）
        _dock?.ApplyLayout();
    }

    private void ApplyLayout()
    {
        // v0.9：底板样式切换改变窗口分层属性 → 重建
        if (_store != null && _dock != null && _store.Config.Settings.DockIconPlate != _currentPlate)
        {
            _currentPlate = _store.Config.Settings.DockIconPlate;
            RecreateWindows();
            return;
        }
        _dock?.ApplyLayout();
        _dock?.ApplyTopmost();     // v0.7：Dock 置顶开关实时生效
        _dock?.ApplyIconPlate();   // v0.8：图标悬停底板实时生效
        _dock?.ApplyWidgets();     // v1.9：时钟/监控组件开关
        ApplyDeskCardVisibility(); // v1.36：桌面组件卡片
        _panel?.ApplyScale();      // v0.6：面板缩放实时生效
    }

    private void RecreateWindows()
    {
        if (_vm is null || _store is null) return;
        var wasPanelVisible = _panel is { IsVisible: true };
        _hotzone?.Stop();
        _panel?.Close();
        _dock?.Close();

        _panel = new PanelWindow(_vm);
        _dock = new DockBarWindow(_vm, ShowPanel, OpenSettings);
        _panel.SetDock(_dock);
        _dock.Opacity = _store.Config.Settings.Opacity;
        _panel.ApplyScale();
        ReloadDeskCards();

        if (_hotzone != null)
        {
            _hotzone.Panel = _panel;
            _hotzone.Dock = _dock;
        }
        _dock.Show();
        MainWindow = _dock;
        _hotzone?.Start();
        if (wasPanelVisible) _panel.ShowPanel();
    }

    // ---------- v1.36：桌面组件卡片 ----------

    private void CreateDeskCards()
    {
        if (_vm is null || _store is null) return;
        var s = _store.Config.Settings;

        _deskClock = new DeskCardWindow(_vm, DeskCardWindow.CardKind.Clock);
        _deskClock.SetOpenSettings(OpenSettings);
        _deskMonitor = new DeskCardWindow(_vm, DeskCardWindow.CardKind.Monitor);
        _deskMonitor.SetOpenSettings(OpenSettings);
        _deskClock._monitorWindow = _deskMonitor;

        // 先显示再按设置决定是否隐藏（保证尺寸测量正确）
        ApplyDeskCardVisibility();
    }

    private void ReloadDeskCards()
    {
        try { _deskClock?.Close(); } catch { }
        try { _deskMonitor?.Close(); } catch { }
        _deskClock = null;
        _deskMonitor = null;
        CreateDeskCards();
    }

    /// <summary>设置变化时调用：开关 / 样式 / 位置。</summary>
    private void ApplyDeskCardVisibility()
    {
        if (_store is null) return;
        var s = _store.Config.Settings;

        ApplyCard(_deskClock, s.DeskClockEnabled, s.DeskClockX, s.DeskClockY);
        ApplyCard(_deskMonitor, s.DeskMonitorEnabled, s.DeskClockX,
            s.DeskClockY + Math.Max(_deskClock?.ActualHeight ?? 120, 120) + 14);
    }

    private static void ApplyCard(DeskCardWindow? card, bool enabled, double left, double top)
    {
        if (card is null) return;
        if (!enabled)
        {
            card.Hide();
            return;
        }

        if (!card.IsVisible)
        {
            // 先给出位置再 Show：WPF 在 Show 时才应用 Left/Top
            card.Left = left;
            card.Top = top;
            card.Show();
        }
        else
        {
            // 已显示时不强制改位置（用户可能刚拖过）
            card.Refresh();
        }
    }

    /// <summary>
    /// v1.37：桌面图标显隐交给 Windows 自己 —— 外部程序在 Win11 24H2 上无法可靠切换
    /// （WM_COMMAND 0x7402 返回成功但不执行、注册表 HideIcons 不重读、移动图标层会被复位）。
    /// 这里只告诉用户系统自带的位置，避免我们再造一个有坑的开关。
    /// </summary>
    private void ShowDesktopIconHint()
    {
        MessageBox.Show(
            "隐藏桌面图标用的是 Windows 自带功能：" + Environment.NewLine + Environment.NewLine +
            "  在桌面空白处右键 → 查看 → 取消勾选「显示桌面图标」" + Environment.NewLine + Environment.NewLine +
            "（Windows 11 也可在「设置 → 个性化 → 主题 → 桌面图标设置」里调整）" + Environment.NewLine + Environment.NewLine +
            "快捷方式不会被删除，随时用同样方式恢复。",
            "隐藏桌面图标", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSettings()
    {
        if (_settings != null)
        {
            try { _settings.Activate(); } catch { /* 已关闭等场景 */ }
            return;
        }
        _settings = new SettingsWindow(_vm!, ApplyHotkey, ApplyOpacity, ApplyAppearance, ApplyLayout);
        _settings.ShowDialog();
        _settings = null;
    }

    // ---------- 托盘（F34）----------

    private void SetupTray()
    {
        _trayIcon = CreateTrayIcon();
        _tray = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "QuickPanel",
            Visible = true,
        };

        RebuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowPanel();
    }

    /// <summary>F34 v0.6：托盘快速分组入口（分组变化时重建）。</summary>
    private void RebuildTrayMenu()
    {
        if (_tray is null || _vm is null) return;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示面板", null, (_, _) => ShowPanel());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        foreach (var group in _vm.Groups)
        {
            var g = group;
            var label = $"{g.Name}（{g.Items.Count}）";
            menu.Items.Add(label, null, (_, _) =>
            {
                _vm.SelectedGroup = g;
                ShowPanel();
            });
        }

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("如何隐藏桌面图标…", null, (_, _) => ShowDesktopIconHint());
        menu.Items.Add("备份配置", null, (_, _) =>
        {
            try
            {
                var path = _store!.Backup();
                MessageBox.Show($"已备份到：\n{path}", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"备份失败：{ex.Message}", "QuickPanel",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        menu.Items.Add("打开配置目录", null, (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = ConfigStore.DataDir, UseShellExecute = true });
            }
            catch { /* 打开失败静默 */ }
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;
    }

    private static SD.Icon CreateTrayIcon()
    {
        // v1.31：优先用打包的多尺寸图标，与 exe/窗口一致；失败再退回纯色绘制
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/QuickPanel.ico");
            using var stream = GetResourceStream(uri)?.Stream;
            if (stream is not null)
                return new SD.Icon(stream, new SD.Size(32, 32));
        }
        catch { /* 资源缺失时走回退 */ }

        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using var path = new SD.Drawing2D.GraphicsPath();
            path.AddArc(2, 2, 10, 10, 180, 90);
            path.AddArc(20, 2, 10, 10, 270, 90);
            path.AddArc(20, 20, 10, 10, 0, 90);
            path.AddArc(2, 20, 10, 10, 90, 90);
            path.CloseFigure();
            using var brush = new SD.SolidBrush(SD.Color.FromArgb(0xFF, 0x7C, 0x6F, 0xFF));
            g.FillPath(brush, path);
            using var font = new SD.Font("Segoe UI", 16f, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var format = new SD.StringFormat
            {
                Alignment = SD.StringAlignment.Center,
                LineAlignment = SD.StringAlignment.Center,
            };
            g.DrawString("Q", font, SD.Brushes.White, new SD.RectangleF(0, 2, 32, 30), format);
        }
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    private static void LogError(object? err)
    {
        try
        {
            File.AppendAllText(Path.Combine(ConfigStore.DataDir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]{Environment.NewLine}{err}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败静默
        }
    }
}
