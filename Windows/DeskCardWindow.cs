using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickPanel.Services;
using QuickPanel.ViewModels;

namespace QuickPanel.Windows;

/// <summary>
/// v1.36：桌面小组件卡片 —— 独立无边框小窗，浮在壁纸上，可自由拖动。
/// 与 Dock 里的组件不同：这是**自己的框**，位置/大小/配色独立，不跟 Dock 绑在一起。
/// 只在桌面层级显示（默认非置顶），切到别的程序会被自然盖住。
/// </summary>
public class DeskCardWindow : Window
{
    public enum CardKind { Clock, Monitor }

    private readonly MainViewModel _vm;
    private readonly CardKind _kind;
    private readonly Border _root;
    private readonly TextBlock _primary;
    private readonly TextBlock _secondary;
    private readonly TextBlock _tertiary;
    private readonly TextBlock _caption;
    private readonly DispatcherTimer _timer;
    private SystemMonitor? _monitor;
    private int _tick;

    private bool _dragging;
    private Point _dragStart;
    private double _startLeft;
    private double _startTop;
    private bool _movedEnough;

    public DeskCardWindow(MainViewModel vm, CardKind kind)
    {
        _vm = vm;
        _kind = kind;

        Title = kind == CardKind.Clock ? "QuickPanel 时钟" : "QuickPanel 监控";
        Icon = null;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        // v1.36：WPF 无法可靠地插到"壁纸之上、其它窗口之下"这一层
        // （SetParent 会被桌面重绘覆盖，SetWindowPos 会被多屏坐标带偏）。
        // 折中：置顶显示，但通过"只在桌面显示"让它在切换程序时自动淡出。
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // 高度自适应内容（宽度由设置控制）
        SizeToContent = SizeToContent.Height;
        MinWidth = 168;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

        _caption = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65,
            Margin = new Thickness(0, 0, 0, 6),
            Text = kind == CardKind.Clock ? "时间" : "系统",
        };

        _primary = new TextBlock
        {
            FontSize = kind == CardKind.Clock ? 34 : 22,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        };

        _secondary = new TextBlock
        {
            FontSize = kind == CardKind.Clock ? 12 : 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0.8,
            TextWrapping = TextWrapping.NoWrap,
        };

        _tertiary = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = 0.8,
            Visibility = kind == CardKind.Monitor ? Visibility.Visible : Visibility.Collapsed,
        };

        stack.Children.Add(_caption);
        stack.Children.Add(_primary);
        stack.Children.Add(_secondary);
        stack.Children.Add(_tertiary);

        _root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(0),
            Child = stack,
            Cursor = Cursors.SizeAll,
        };
        Content = _root;

        Loaded += OnLoaded;
        MouseLeftButtonDown += (_, e) => BeginDrag(e);
        MouseMove += (_, e) => ContinueDrag(e);
        MouseLeftButtonUp += (_, e) => EndDrag(e);
        MouseRightButtonUp += (_, e) => ShowMenu();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { Update(); TickDesktopOnly(); };

        ApplyStyle();
        Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = _vm.Store.Config.Settings;

        // 只有当 App 没显式指定位置时才自己摆放（避免把外部设置覆盖掉）
        if (double.IsNaN(Left) || double.IsNaN(Top))
            ApplyConfiguredPosition();

        // v1.36：加载时不做坐标纠正 —— 之前用"鼠标所在屏"的工作区判断，
        // 鼠标在副屏时会把主屏上的合法位置误判为越界，两张卡一起被搬到同一处。
        // 位置不对交给用户拖一下即可。
        _root.Opacity = 0;
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        _timer.Start();
    }

    /// <summary>按配置摆放：时钟卡用 DeskClockX/Y，监控卡纵向排在其下方。</summary>
    public void ApplyConfiguredPosition()
    {
        var s = _vm.Store.Config.Settings;
        if (_kind == CardKind.Clock)
        {
            Left = s.DeskClockX;
            Top = s.DeskClockY;
        }
        else
        {
            Left = s.DeskClockX;
            var ch = _clockHeight > 0 ? _clockHeight : 118;
            Top = s.DeskClockY + ch + 14;
        }
    }

    /// <summary>由 App 注入时钟卡实际高度，用于监控卡纵向错开。</summary>
    public double _clockHeight = 118;

    /// <summary>v1.36：只在桌面时显示 —— 前台是别的程序就淡出，不挡视线。</summary>
    private void TickDesktopOnly()
    {
        var s = _vm.Store.Config.Settings;
        if (!s.DeskCardDesktopOnly) { _root.Opacity = 1; return; }

        var onDesktop = IsDesktopForeground();
        var target = onDesktop ? 1.0 : 0.0;
        if (Math.Abs(_root.Opacity - target) < 0.02) return;
        _root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(200)));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);

    private static bool IsDesktopForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            var sb = new System.Text.StringBuilder(128);
            GetClassName(hwnd, sb, 128);
            var cls = sb.ToString();
            return cls is "Progman" or "WorkerW" or "Shell_TrayWnd";
        }
        catch { return true; }
    }

    /// <summary>设置变化后重新取样式与开关。</summary>
    public void Refresh()
    {
        var s = _vm.Store.Config.Settings;
        var enabled = _kind == CardKind.Clock ? s.DeskClockEnabled : s.DeskMonitorEnabled;
        if (!enabled) { Hide(); _timer.Stop(); return; }

        ApplyStyle();
        Update();
        if (!IsVisible) Show();
        _timer.Start();
    }

    private void ApplyStyle()
    {
        var s = _vm.Store.Config.Settings;
        var style = s.DeskCardStyle;

        Color bg, text, sub;
        Brush? border = null;

        switch (style?.ToLowerInvariant())
        {
            case "light":
                bg = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF);
                text = Color.FromRgb(0x1B, 0x1F, 0x2A);
                sub = Color.FromRgb(0x5A, 0x61, 0x72);
                border = new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0));
                break;
            case "dark":
                bg = Color.FromArgb(0xF0, 0x1A, 0x1A, 0x1F);
                text = Color.FromRgb(0xF2, 0xF3, 0xF7);
                sub = Color.FromRgb(0xB9, 0xBD, 0xC7);
                border = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                break;
            default: // glass
                bg = Color.FromArgb(0xB8, 0x18, 0x18, 0x20);
                text = Color.FromRgb(0xF5, 0xF6, 0xFA);
                sub = Color.FromRgb(0xC3, 0xC7, 0xD2);
                border = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
                break;
        }

        _root.Background = new SolidColorBrush(bg);
        _root.BorderBrush = border;
        _root.BorderThickness = border is null ? new Thickness(0) : new Thickness(1);
        _root.Effect = new DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 0,
            Opacity = 0.28,
            Color = Colors.Black,
        };

        _caption.Foreground = new SolidColorBrush(sub) { Opacity = 0.75 };
        _primary.Foreground = new SolidColorBrush(text);
        _secondary.Foreground = new SolidColorBrush(sub);
        _tertiary.Foreground = new SolidColorBrush(sub);

        var scale = Math.Clamp(s.DeskCardScale, 0.8, 1.8);
        _root.LayoutTransform = new ScaleTransform(scale, scale);
        _root.MinWidth = Math.Clamp(s.DeskCardWidth, 150, 360);
    }

    private void Update()
    {
        try
        {
            var s = _vm.Store.Config.Settings;
            var now = DateTime.Now;

            if (_kind == CardKind.Clock)
            {
                _primary.Text = s.ClockShowSeconds ? now.ToString("HH:mm:ss") : now.ToString("HH:mm");
                _secondary.Text = now.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
            }
            else
            {
                _monitor ??= new SystemMonitor();
                if (_tick++ % 2 == 0)
                {
                    _monitor.Update();
                    _primary.Text = $"CPU {_monitor.CpuPercent:0}%";
                    _secondary.Text = $"内存 {_monitor.MemoryPercent:0}%";
                    _tertiary.Text = $"↓ {FormatNet(_monitor.NetDownKbps)}/s";
                }
            }
        }
        catch { /* 更新失败不影响卡片 */ }
    }

    private static string FormatNet(double kbps)
        => kbps >= 1024 ? $"{kbps / 1024:0.0} MB" : $"{kbps:0} KB";

    // ---------- 自由拖动 ----------

    private void BeginDrag(MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _dragging = true;
        _movedEnough = false;
        _dragStart = e.GetPosition(this);
        _startLeft = Left;
        _startTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void ContinueDrag(MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        if (!_movedEnough && Math.Abs(dx) + Math.Abs(dy) > 4) _movedEnough = true;
        if (!_movedEnough) return;

        Left = _startLeft + dx;
        Top = _startTop + dy;
    }

    private void EndDrag(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (!_movedEnough) return;
        // 拖到屏幕外时夹回**窗口当前所在屏**的工作区
        var wa = ScreenHelper.GetWorkAreaForWindow(this);
        Left = Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - ActualWidth));
        Top = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - ActualHeight));

        var s = _vm.Store.Config.Settings;
        if (_kind == CardKind.Clock)
        {
            s.DeskClockX = Left;
            s.DeskClockY = Top;
            // 监控卡片跟着保持相对偏移（否则会错位）
            if (_monitorWindow is { IsVisible: true } mw)
            {
                mw._clockHeight = ActualHeight;
                mw.ApplyConfiguredPosition();
            }
        }
        _vm.SaveConfig();
    }

    /// <summary>由 App 注入另一张卡片，拖动时钟时带动监控卡。</summary>
    public DeskCardWindow? _monitorWindow;

    private void ShowMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _root, Placement = PlacementMode.Mouse };

        var toggle = new MenuItem
        {
            Header = _kind == CardKind.Clock ? "隐藏时钟卡片" : "隐藏监控卡片",
            IsCheckable = false,
        };
        toggle.Click += (_, _) =>
        {
            var s = _vm.Store.Config.Settings;
            if (_kind == CardKind.Clock) s.DeskClockEnabled = false;
            else s.DeskMonitorEnabled = false;
            _vm.SaveConfig();
            Refresh();
        };
        menu.Items.Add(toggle);

        var reset = new MenuItem { Header = "重置位置" };
        reset.Click += (_, _) =>
        {
            var s = _vm.Store.Config.Settings;
            s.DeskClockX = 80;
            s.DeskClockY = 80;
            _vm.SaveConfig();
            Left = s.DeskClockX;
            Top = _kind == CardKind.Clock ? s.DeskClockY : s.DeskClockY + Math.Max(ActualHeight, 120) + 14;
        };
        menu.Items.Add(reset);

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "组件设置…" };
        settings.Click += (_, _) => _openSettings?.Invoke();
        menu.Items.Add(settings);

        menu.IsOpen = true;
    }

    private Action? _openSettings;

    /// <summary>App 注入：右键菜单里的"组件设置"跳到设置窗口。</summary>
    public void SetOpenSettings(Action openSettings) => _openSettings = openSettings;
}
