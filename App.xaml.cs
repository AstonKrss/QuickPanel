using System.Diagnostics;
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
        _hotzone.Start();
        _guard.StartActivateListener(() => Dispatcher.Invoke(ShowPanel));

        // v1.30：上次选了"桌面留白"就恢复（只在开启时强制隐藏，不与系统自身设置抢）
        if (_store.Config.Settings.DesktopIconsHidden)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => DesktopIcons.SetHidden(true)));
        }

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

    protected override void OnExit(ExitEventArgs e)
    {
        // v1.30：退出时把桌面图标还回去 —— "桌面留白"只在 QuickPanel 运行时生效，
        // 避免用户关掉程序后既看不到图标、又点不了桌面右键（DefView 被隐藏）。
        try
        {
            if (_store?.Config.Settings.DesktopIconsHidden == true)
                DesktopIcons.SetHidden(false);
        }
        catch { /* 退出期失败忽略 */ }

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

    /// <summary>v1.30：桌面留白开关（托盘快速入口）。</summary>
    private void ToggleDesktopIcons()
    {
        if (_store is null) return;
        var target = !DesktopIcons.IsHidden();
        if (!DesktopIcons.SetHidden(target))
        {
            MessageBox.Show("切换桌面图标失败：找不到桌面窗口（Explorer 可能未运行）。",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.Config.Settings.DesktopIconsHidden = target;
        _vm?.SaveConfig();
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
        var desktopToggle = menu.Items.Add("隐藏桌面图标（桌面留白）", null, (_, _) => ToggleDesktopIcons());
        menu.Opening += (_, _) =>
            desktopToggle.Text = DesktopIcons.IsHidden() ? "显示桌面图标" : "隐藏桌面图标（桌面留白）";
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
