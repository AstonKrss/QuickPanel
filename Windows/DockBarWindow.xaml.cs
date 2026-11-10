using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using QuickPanel.Services;
using QuickPanel.ViewModels;

namespace QuickPanel.Windows;

public partial class DockBarWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action _showPanel;
    private readonly Action _openSettings;
    private readonly List<(Border Border, UIElement Container)> _pinBorders = new();
    private readonly Dictionary<Border, Point> _pinDragAnchors = new();
    private bool _hiddenForFullscreen;
    private bool _oleDragging;
    private bool _transparent;
    private bool _plate = true;
    private bool _bare; // v0.9：纯图标模式（整条 Dock 无底无框）

    // v1.9：组件（时钟 + 系统监控）
    private readonly SystemMonitor _monitor = new();
    private DispatcherTimer? _widgetTimer;
    private int _monitorTick;

    public DockBarWindow(MainViewModel vm, Action showPanel, Action openSettings)
    {
        InitializeComponent();
        _vm = vm;
        _showPanel = showPanel;
        _openSettings = openSettings;
        DataContext = vm;
        // v1.1：acrylic=毛玻璃背板；transparent/invisible=分层窗口
        _transparent = true; // v1.27：统一使用分层窗口（DWM 背板会涂满整个窗口矩形）
        _plate = vm.Store.Config.Settings.DockIconPlate;
        _bare = !_plate; // v0.9：纯图标 = 整条 Dock 无底，需分层窗口
        AllowsTransparency = true;
        // v1.1：透明/隐形模式下关闭 DWM 玻璃垫层——GlassFrameThickness=-1 会让整个窗口
        // 矩形下垫一层玻璃膜（比壁纸亮/雾的"色差矩形"根源），分层透明不需要它
        var chrome = WindowChrome.GetWindowChrome(this);
        if (_transparent || _bare)
        {
            // v1.2：分层窗口彻底移除 WindowChrome —— 只要有 chrome，DWM 就可能画边框/玻璃
            WindowChrome.SetWindowChrome(this, null);
        }
        else if (chrome != null)
        {
            chrome.GlassFrameThickness = new Thickness(0);
        }
        // F20 v0.7：默认不置顶（只在桌面层级显示），设置可开启
        Topmost = vm.Store.Config.Settings.DockTopmost;

        Loaded += (_, _) => { ApplyLayout(initial: true); RebuildPinVisuals(); ApplyWidgets(); };
        SizeChanged += (_, _) => Reposition();
        vm.DockPins.CollectionChanged += OnPinsChanged;
        // ItemsSource 绑定晚于 Loaded 才填充（见 §6.3），以容器生成事件为准重建引用
        PinHost.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (PinHost.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                RebuildPinVisuals();
        };
        // v1.3：分组按钮是独立 ItemsControl，容器生成时机不同 —— 必须单独监听，
        // 否则 ApplyBareText 跑在容器生成前，分组文字拿不到 TextBlock（一直保持浅灰）
        GroupButtons.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (GroupButtons.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                ApplyBareText();
        };

        SourceInitialized += (_, _) =>
        {
            // v1.27：所有模式统一使用分层窗口（AllowsTransparency），不再用 DWM 背板。
            // 原因：DWM SystemBackdrop 会把**整个窗口矩形**涂成玻璃，而我们为了悬停放大
            // 需要在胶囊上方留一段透明余量 —— 那一段也会被涂成玻璃，看起来就是
            // "胶囊上下多出一大块没用的地方"。真毛玻璃在这种布局下无解，改为
            // 分层窗口内的半透明色板（视觉接近，且不会有多余矩形）。
            var invisible = Theme.NearInvisible;

            if (_bare)
            {
                // 纯图标：无底无框；alpha=1 隐形薄膜保点击（alpha=0 会穿透）
                PlateBorder.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                PlateBorder.BorderThickness = new Thickness(0);
            }
            else if (invisible)
            {
                PlateBorder.BorderThickness = new Thickness(0);
            }

            // Dock 一律不挂 DropShadowEffect —— Effect 的中间位图会以边界裁切内容（§6.3）
        };
    }

    private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RebuildPinVisuals());

    /// <summary>
    /// 可见 Dock 胶囊的屏幕矩形（不含顶部隐形余量）。
    /// 热区判定与面板定位都用它，避免余量区产生"幽灵热区"。
    /// </summary>
    public Win32.RECT GetScreenRect()
    {
        Win32.GetWindowRect(new WindowInteropHelper(this).Handle, out var win);
        try
        {
            var origin = PlateBorder.PointToScreen(new Point(0, 0));
            var w = PlateBorder.ActualWidth;
            var h = PlateBorder.ActualHeight;
            if (w > 0 && h > 0)
            {
                return new Win32.RECT
                {
                    Left = (int)origin.X,
                    Top = (int)origin.Y,
                    Right = (int)(origin.X + w),
                    Bottom = (int)(origin.Y + h),
                };
            }
        }
        catch
        {
            // 未加载完成时回退窗口矩形
        }
        return win;
    }

    /// <summary>热区真实命中检测用：Dock 顶层窗口句柄。</summary>
    public IntPtr GetHwnd() => new WindowInteropHelper(this).Handle;

    /// <summary>F20 v0.7：Dock 置顶开关（默认不置顶，只在桌面层级）。</summary>
    public void ApplyTopmost()
        => Topmost = _vm.Store.Config.Settings.DockTopmost;

    /// <summary>F35：全屏时由 HotZoneService 收起，退出后恢复。</summary>
    public void CollapseForFullscreen()
    {
        if (!IsVisible) return;
        _hiddenForFullscreen = true;
        Hide();
    }

    public void RestoreFromFullscreen()
    {
        if (!_hiddenForFullscreen) return;
        _hiddenForFullscreen = false;
        Reposition();
        Show();
    }

    // ---------- v1.4：仅在桌面显示 ----------

    private bool _hiddenForDesktop;

    /// <summary>打开其他软件时隐藏 Dock。</summary>
    public void CollapseForDesktop()
    {
        if (!IsVisible) return;
        _hiddenForDesktop = true;
        Hide();
    }

    /// <summary>回到桌面时恢复 Dock。</summary>
    public void RestoreFromDesktop()
    {
        if (!_hiddenForDesktop) return;
        _hiddenForDesktop = false;
        Reposition();
        Show();
    }

    /// <summary>F26：应用图标缩放与位置模式；initial=true 时恢复持久化的自由位置。</summary>
    public void ApplyLayout(bool initial = false)
    {
        var settings = _vm.Store.Config.Settings;
        var scale = Math.Clamp(settings.IconScale, 0.6, 1.6);
        ContentScale.ScaleX = scale;
        ContentScale.ScaleY = scale;
        _plate = settings.DockIconPlate;

        // v1.6：顶部余量用 Grid 行高承载（SizeToContent 会忽略 Border.Margin，导致窗口不增高）
        // 溢出 = 44·s·0.32/2 + 8·s ≈ 15·s（s=图标缩放），+12 兜底 DPI 取整
        // v1.12：headroom 按实际图钉尺寸 + 悬停倍数 + 上浮距离计算
        var pinSize = Math.Clamp(settings.PinSize, 24, 64);
        var hoverScale = Math.Clamp(settings.HoverScale, 1.0, 1.8);
        var hoverLift = Math.Clamp(settings.HoverLift, 0, 20);
        var overflow = pinSize * (hoverScale - 1) / 2 + hoverLift; // 放大溢出 + 上浮
        var headroom = Math.Max(20, overflow * scale + 12);
        HeadroomRow.Height = new GridLength(Math.Round(headroom));
        InvalidateMeasure();
        UpdateLayout();


        RebuildPinVisuals();

        if (settings.DockPositionMode == "free")
        {
            if (initial)
            {
                Left = settings.DockX;
                Top = settings.DockY;
                // v1.16：若保存位置在已断开的显示器上，拉回可见区域
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        var area = ScreenHelper.GetWorkAreaAtCursor();
                        var before = (Left, Top);
                        ScreenHelper.ClampWindow(this, area);
                        if (before != (Left, Top))
                        {
                            settings.DockX = Left;
                            settings.DockY = Top;
                            _vm.SaveConfig();
                        }
                    }
                    catch { /* 夹取失败忽略 */ }
                }));
            }
            // 非初始：保持当前位置，不跳位
        }
        else
        {
            Reposition();
        }
    }

    /// <summary>F20 v0.8：底板样式切换（带底板/纯图标）。</summary>
    public void ApplyIconPlate() => RebuildPinVisuals();

    // ---------- v1.9：时钟 + 系统监控组件 ----------

    /// <summary>按设置显示/隐藏时钟与监控，并启动/停止定时器。</summary>
    public void ApplyWidgets()
    {
        var s = _vm.Store.Config.Settings;
        var showClock = s.ShowClock;
        var showMonitor = s.ShowSystemMonitor;

        // v1.10：组件字体独立缩放
        var ws = Math.Clamp(s.WidgetScale, 0.7, 1.6);
        WidgetScaleTf.ScaleX = ws;
        WidgetScaleTf.ScaleY = ws;

        WidgetPanel.Visibility = (showClock || showMonitor) ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
        MonitorPanel.Visibility = showMonitor ? Visibility.Visible : Visibility.Collapsed;

        ApplyBareText(); // v1.11：组件文字颜色跟随自定义色

        if (!showClock && !showMonitor)
        {
            _widgetTimer?.Stop();
            _widgetTimer = null;
            return;
        }

        UpdateWidgets();
        if (_widgetTimer is null)
        {
            _widgetTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _widgetTimer.Tick += (_, _) => UpdateWidgets();
            _widgetTimer.Start();
        }
    }

    private void UpdateWidgets()
    {
        try
        {
            var s = _vm.Store.Config.Settings;
            var now = DateTime.Now;

            if (s.ShowClock)
            {
                ClockText.Text = s.ClockShowSeconds
                    ? now.ToString("HH:mm:ss")
                    : now.ToString("HH:mm");
                DateText.Text = now.ToString("M月d日 ddd", new System.Globalization.CultureInfo("zh-CN"));
            }

            if (s.ShowSystemMonitor)
            {
                // 监控每 2 秒采样一次（CPU 差分需要间隔）
                if (_monitorTick++ % 2 == 0)
                {
                    _monitor.Update();
                    MonitorLine1.Text = $"CPU {_monitor.CpuPercent:0}%";
                    MonitorLine2.Text = $"内存 {_monitor.MemoryPercent:0}% · ↓{FormatNet(_monitor.NetDownKbps)}";
                }
            }
        }
        catch { /* 组件更新失败不影响 Dock */ }
    }

    private static string FormatNet(double kbps)
        => kbps >= 1024 ? $"{kbps / 1024:0.0}M" : $"{kbps:0}K";

    /// <summary>F24：实时居中（仅 bottomCenter 模式；自由模式保持用户位置）。</summary>
    private void Reposition()
    {
        if (_vm.Store.Config.Settings.DockPositionMode == "free") return;
        // v1.16：在 Dock 当前所在显示器上居中（多屏支持）
        var wa = ScreenHelper.GetWorkAreaForWindow(this);
        Top = wa.Bottom - ActualHeight;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
    }

    private void RebuildPinVisuals()
    {
        _pinBorders.Clear();
        var s = _vm.Store.Config.Settings;
        var pinSize = Math.Clamp(s.PinSize, 24, 64);
        var spacing = Math.Clamp(s.PinSpacing, 0, 16);
        var radius = Math.Clamp(s.PinCornerRadius, 0, 22);

        for (var i = 0; i < PinHost.Items.Count; i++)
        {
            if (PinHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (FindDescendant<Border>(container) is not { } border) continue;

            // v1.12：应用样式细调
            border.Width = pinSize;
            border.Height = pinSize;
            border.CornerRadius = new CornerRadius(radius);
            border.Margin = new Thickness(spacing / 2, 0, spacing / 2, 0);
            border.Background = Brushes.Transparent; // 底板统一由 Enter/Leave 控制

            // 图标按比例缩放（图钉尺寸的 73%）
            if (FindDescendant<Image>(border) is { } img)
            {
                var iconSize = Math.Round(pinSize * 0.73);
                img.Width = iconSize;
                img.Height = iconSize;

                // v1.15：隐形模式下给图标加柔和阴影，复杂背景上可辨认轮廓
                img.Effect = _bare
                    ? new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.55,
                    }
                    : null;
            }

            if (container is UIElement ui)
                _pinBorders.Add((border, ui));
        }
        ApplyBareText();
    }

    // ---------- v1.1/v1.2：裸 Dock（纯图标）—— 去掉分组按钮底板 + 文字加深描光 ----------

    private void ApplyBareText()
    {
        var bare = !_plate;
        // v1.4：纯图标模式文字颜色由用户自定义（默认纯黑）
        var custom = Theme.ParseColor(_vm.Store.Config.Settings.DockTextColor, Colors.Black);
        var textBrush = bare
            ? new SolidColorBrush(custom)
            : (Brush)FindResource("TextPrimaryBrush");

        // v1.15：隐形模式下给文字加柔和外发光，保证复杂背景（代码/图片）上可读
        var textGlow = bare ? MakeReadabilityGlow() : null;

        GripText.Foreground = textBrush;
        GripText.Effect = textGlow;
        GripText.FontWeight = bare ? FontWeights.Bold : FontWeights.Normal;
        GearText.Foreground = textBrush;
        GearText.Effect = textGlow;

        // v1.11：时钟/日期/监控文字同样跟随自定义颜色（此前漏掉，一直是主题默认色）
        ClockText.Foreground = textBrush;
        DateText.Foreground = bare ? textBrush : (Brush)FindResource("TextSecondaryBrush");
        MonitorLine1.Foreground = bare ? textBrush : (Brush)FindResource("TextSecondaryBrush");
        MonitorLine2.Foreground = bare ? textBrush : (Brush)FindResource("TextSecondaryBrush");
        ClockText.Effect = textGlow;
        DateText.Effect = textGlow;
        MonitorLine1.Effect = textGlow;
        MonitorLine2.Effect = textGlow;
        if (bare)
        {
            ClockText.FontWeight = FontWeights.Bold;
            DateText.FontWeight = FontWeights.SemiBold;
            MonitorLine1.FontWeight = FontWeights.SemiBold;
            MonitorLine2.FontWeight = FontWeights.SemiBold;
        }

        for (var i = 0; i < GroupButtons.Items.Count; i++)
        {
            if (GroupButtons.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (FindDescendant<TextBlock>(container) is not { } tb) continue;
            tb.Foreground = textBrush;
            tb.Effect = null;
            if (bare)
            {
                tb.FontWeight = FontWeights.Bold;
                tb.FontSize = 14;
            }

            // v1.2：纯图标模式下分组按钮也不能有底板——之前漏掉，是"还有一圈框"的真正来源
            if (bare && FindDescendant<Border>(container) is { } plateBorder)
            {
                // 清空 Style 里的 Background 设置，否则触发器会重新画底板
                plateBorder.Background = Brushes.Transparent;
                plateBorder.Style = null;
            }
        }
    }

    /// <summary>v1.15：文字可读性外发光 —— 深色字配白色光晕，浅色字配深色光晕。</summary>
    private DropShadowEffect MakeReadabilityGlow()
    {
        var c = Theme.ParseColor(_vm.Store.Config.Settings.DockTextColor, Colors.Black);
        var lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        return new DropShadowEffect
        {
            Color = lum < 128 ? Colors.White : Colors.Black,
            BlurRadius = 6,
            ShadowDepth = 0,
            Opacity = 0.85,
        };
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>v1.20：取指定分组按钮在屏幕上的左边缘 X（DIP）；找不到返回 null。</summary>
    public double? GetGroupButtonScreenX(GroupVM? group)
    {
        if (group is null) return null;
        try
        {
            for (var i = 0; i < GroupButtons.Items.Count; i++)
            {
                if (!ReferenceEquals(GroupButtons.Items[i], group)) continue;
                if (GroupButtons.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                if (FindDescendant<Border>(c) is not { } b) continue;

                // 用本窗口的 DPI 把物理像素换算成 DIP
                var origin = b.PointToScreen(new Point(0, 0));
                var src = PresentationSource.FromVisual(this);
                var toDip = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                return toDip.Transform(origin).X;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    // ---------- F21：悬停分组按钮热切换 ----------

    private GroupVM? FindGroupAt(Point pt)
    {
        for (var i = 0; i < GroupButtons.Items.Count; i++)
        {
            if (GroupButtons.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container) continue;
            if (FindDescendant<Border>(container) is not { } border) continue;
            var origin = border.TransformToAncestor(RootBorder).Transform(new Point());
            var rect = new Rect(origin,
                new Point(origin.X + border.ActualWidth, origin.Y + border.ActualHeight));
            if (rect.Contains(pt)) return GroupButtons.Items[i] as GroupVM;
        }
        return null;
    }

    /// <summary>热区唤出面板前调用：光标停在哪 个分组按钮上，就定位哪个分组。</summary>
    public void SelectGroupUnderCursor()
    {
        var group = FindGroupAt(Mouse.GetPosition(RootBorder));
        if (group != null && !ReferenceEquals(group, _vm.SelectedGroup))
            _vm.SelectedGroup = group;
    }

    /// <summary>F31 v0.8：光标（屏幕坐标）是否停在分组按钮上——热区只在这里才弹面板。</summary>
    public bool IsOverGroupButton(Win32.POINT screenPt)
    {
        try
        {
            var local = RootBorder.PointFromScreen(new Point(screenPt.X, screenPt.Y));
            return FindGroupAt(local) != null;
        }
        catch
        {
            return false;
        }
    }

    private void Dock_MouseMove(object sender, MouseEventArgs e)
    {
        if (_oleDragging) return;

        // F21：滑到分组按钮上即切换面板分组
        var hovered = FindGroupAt(e.GetPosition(RootBorder));
        if (hovered != null && !ReferenceEquals(hovered, _vm.SelectedGroup))
            _vm.SelectedGroup = hovered;
    }

    // ---------- F23 v0.8：悬停放大动效 ----------

    private (ScaleTransform Scale, TranslateTransform Lift)? GetPinTransforms(Border border)
    {
        // DataTemplate 内 Freezable 可能被密封冻结，冻结则换新实例（§6.3）
        if (border.RenderTransform is TransformGroup tg && !tg.IsFrozen)
        {
            var s = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
            var t = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (s != null && t != null) return (s, t);
        }

        var ns = new ScaleTransform(1, 1);
        var nt = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(ns);
        group.Children.Add(nt);
        border.RenderTransform = group;
        return (ns, nt);
    }

    private void AnimatePin(Border border, double toScale, double toLift)
    {
        var pinTf = GetPinTransforms(border);
        if (pinTf is null) return;
        var (scale, lift) = pinTf.Value;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        var sx = new DoubleAnimation(toScale, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        var sy = new DoubleAnimation(toScale, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        var ly = new DoubleAnimation(toLift, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        lift.BeginAnimation(TranslateTransform.YProperty, ly);
    }

    private void Pin_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Border border) return;
        if (_plate)
            border.Background = (Brush)FindResource("CardBrush");
        // v1.1：放大的图钉置顶，避免被右侧邻居盖住
        var entry = _pinBorders.FirstOrDefault(p => ReferenceEquals(p.Border, border));
        if (entry.Container != null)
            Panel.SetZIndex(entry.Container, 100);
        // v1.12：悬停倍数/上浮距离可配置
        var hs = Math.Clamp(_vm.Store.Config.Settings.HoverScale, 1.0, 1.8);
        var hl = Math.Clamp(_vm.Store.Config.Settings.HoverLift, 0, 20);
        AnimatePin(border, hs, -hl);

    }

    private void Pin_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Border border) return;
        border.Background = Brushes.Transparent;
        var entry = _pinBorders.FirstOrDefault(p => ReferenceEquals(p.Border, border));
        if (entry.Container != null)
            Panel.SetZIndex(entry.Container, 0);
        AnimatePin(border, 1.0, 0);
    }

    // ---------- F26：⠿ 自由拖动 ----------

    private void Grip_Down(object sender, MouseButtonEventArgs e) => StartFreeDrag();

    private void Root_Down(object sender, MouseButtonEventArgs e)
    {
        // 仅空白衬垫区域（RootBorder 自身）触发拖动，不影响按钮/图钉
        if (e.OriginalSource == RootBorder && e.LeftButton == MouseButtonState.Pressed)
            StartFreeDrag();
    }

    private void StartFreeDrag()
    {
        var settings = _vm.Store.Config.Settings;
        if (settings.DockPositionMode != "free")
        {
            settings.DockPositionMode = "free";
            _vm.SaveConfig();
        }
        try
        {
            DragMove();
        }
        catch
        {
            return; // 非左键按下状态调用会抛出
        }
        settings.DockX = Left;
        settings.DockY = Top;
        _vm.SaveConfig();
    }

    // ---------- 拖出（F3 v0.5）----------

    private void Pin_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && e.LeftButton == MouseButtonState.Pressed)
            _pinDragAnchors[border] = e.GetPosition(border);
    }

    /// <summary>
    /// v1.17：标准 DragDrop 拖拽 —— 修复此前"排序不生效"（容器重建后引用失效 + 每像素写盘）。
    /// 行为：
    ///   · 在 Dock 内拖 → 重排
    ///   · 拖出 Dock 上方 → 取消钉选（移除）
    ///   · 拖到桌面 → 生成副本（保留）
    /// </summary>
    private void Pin_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || !_pinDragAnchors.TryGetValue(border, out var anchor)) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pinDragAnchors.Remove(border);
            return;
        }

        var pos = e.GetPosition(border);
        if (Math.Abs(pos.X - anchor.X) + Math.Abs(pos.Y - anchor.Y)
            < SystemParameters.MinimumHorizontalDragDistance) return;

        _pinDragAnchors.Remove(border);
        if (border.Tag is not ItemVM item) return;

        // v1.18：只携带自定义数据，不带 FileDrop ——
        // 这样拖到桌面不会被资源管理器接收（避免"复制一份到桌面"），
        // 拖出 Dock 即视为"取消钉选 → 退回原分组"。
        var data = new DataObject();
        data.SetData("QuickPanel.Pin", item);

        // 记录拖拽前所在分组，用于拖出时给出准确提示
        var sourceGroup = item.OwnerGroup
                          ?? _vm.Groups.FirstOrDefault(g => g.Items.Any(i =>
                              string.Equals(i.Model.Target, item.Model.Target, StringComparison.OrdinalIgnoreCase)));

        // v1.19：显示跟随光标的半透明图标 + 源图标淡出
        DragGhost? ghost = null;
        var originalOpacity = border.Opacity;
        try
        {
            var pinSize = Math.Clamp(_vm.Store.Config.Settings.PinSize, 24, 64);
            ghost = new DragGhost(item.Icon, item.Name, pinSize * 1.15);
            border.Opacity = 0.35;   // 源图标淡出，暗示"正在移动"
            AnimatePin(border, 1.0, 0);
        }
        catch { /* ghost 失败不影响拖拽 */ }

        DragDropEffects result;
        try
        {
            result = DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
        }
        finally
        {
            ghost?.Dispose();
            border.Opacity = originalOpacity;
        }

        // 未被任何目标接受 + 光标已离开 Dock → 取消钉选（条目仍在原分组中）
        if (result == DragDropEffects.None)
        {
            var cur = GetCursorScreenPoint();
            var dockRect = GetScreenRect();
            var outside = cur.Y < dockRect.Top - 4 || cur.Y > dockRect.Bottom + 4
                          || cur.X < dockRect.Left - 4 || cur.X > dockRect.Right + 4;
            if (outside)
            {
                _vm.Unpin(item);
                // v1.18：告知用户它回到了哪个分组（不是被删除）
                if (sourceGroup != null)
                    ShowToast($"已取消钉选，仍保留在「{sourceGroup.Name}」分组中");
            }
        }
    }

    // ---------- v1.17：Dock 内拖拽排序 / 拖出取消钉选 ----------

    /// <summary>DragOver 实时记录光标下的图钉（Drop 时直接用，避免坐标反查失败）。</summary>
    private ItemVM? _dragOverTarget;

    private void PinHost_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("QuickPanel.Pin"))
        {
            e.Effects = DragDropEffects.Move;          // Dock 内部重排
            try { _dragOverTarget = FindPinAtPoint(e.GetPosition(PinHost)); }
            catch { _dragOverTarget = null; }
        }
        else if (e.Data.GetDataPresent("QuickPanel.Item"))
            e.Effects = DragDropEffects.Copy;          // 面板条目 → 钉选
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;          // 资源管理器 → 钉选
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>v1.18：轻量提示（不改动 Dock 布局，1.8 秒后自动消失）。</summary>
    private void ShowToast(string message)
    {
        try
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x20, 0x20, 0x24)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Child = new TextBlock { Text = message, FontSize = 12, Foreground = Brushes.White },
            };
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                StaysOpen = true,
                IsOpen = true,
                AllowsTransparency = true,
                VerticalOffset = -12,
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            timer.Tick += (_, _) => { timer.Stop(); popup.IsOpen = false; };
            timer.Start();
        }
        catch { /* 提示失败不影响主流程 */ }
    }

    /// <summary>v1.17：按相对 PinHost 的横向坐标找图钉（图钉水平排列，纵向放宽）。</summary>
    private ItemVM? FindPinAtPoint(Point ptInHost)
    {
        for (var i = 0; i < PinHost.Items.Count; i++)
        {
            if (PinHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            if (FindDescendant<Border>(c) is not { } b) continue;
            try
            {
                var origin = b.TranslatePoint(new Point(0, 0), PinHost);
                if (ptInHost.X >= origin.X - 2 && ptInHost.X <= origin.X + b.ActualWidth + 2)
                    return PinHost.Items[i] as ItemVM;
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    /// <summary>v1.18：按光标相对 PinHost 的 X 坐标，算出应插入的索引（左半区插前，右半区插后）。</summary>
    private int ComputeInsertIndex(Point ptInHost)
    {
        for (var i = 0; i < PinHost.Items.Count; i++)
        {
            if (PinHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            if (FindDescendant<Border>(c) is not { } b) continue;
            try
            {
                var origin = b.TranslatePoint(new Point(0, 0), PinHost);
                var mid = origin.X + b.ActualWidth / 2;
                if (ptInHost.X < mid) return i;       // 落在该图钉左半区 → 插到它前面
            }
            catch { /* 忽略 */ }
        }
        return _vm.DockPins.Count; // 落到最右侧 → 追加末尾
    }

    private void PinHost_Drop(object sender, DragEventArgs e)
    {
        // 场景 1：Dock 内部重排
        if (e.Data.GetData("QuickPanel.Pin") is ItemVM dragged)
        {
            var target = _dragOverTarget ?? FindPinAtPoint(e.GetPosition(PinHost));
            _dragOverTarget = null;

            if (target != null && !ReferenceEquals(target, dragged))
            {
                var from = _vm.DockPins.IndexOf(dragged);
                var to = _vm.DockPins.IndexOf(target);
                if (from >= 0 && to >= 0 && from != to)
                {
                    _vm.DockPins.Move(from, to);
                    _vm.SaveConfig();
                }
            }
            e.Handled = true;
            return;
        }

        // 场景 2：从面板拖来的条目 → 钉选（v1.18：按光标位置插入，不再只追加末尾）
        if (e.Data.GetData("QuickPanel.Item") is ItemVM panelItem)
        {
            var insertAt = ComputeInsertIndex(e.GetPosition(PinHost));
            _vm.PinExistingItem(panelItem, insertAt);
            e.Handled = true;
            return;
        }

        // 场景 3：从资源管理器拖入文件 → 钉选
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _vm.PinToDock(files);
            e.Handled = true;
        }
    }

    /// <summary>找到屏幕坐标下的图钉条目。</summary>
    private ItemVM? FindPinAtScreenPoint(Point screenPt)
    {
        for (var i = 0; i < PinHost.Items.Count; i++)
        {
            if (PinHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            if (FindDescendant<Border>(c) is not { } b) continue;
            try
            {
                var origin = b.PointToScreen(new Point(0, 0));
                var rect = new Rect(origin, new Size(b.ActualWidth, b.ActualHeight));
                if (rect.Contains(screenPt)) return PinHost.Items[i] as ItemVM;
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    private static Point GetCursorScreenPoint()
    {
        Win32.GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    /// <summary>
    /// v1.17：Dock 自己作为放下目标时，若光标已离开 Dock 上方 → 取消钉选。
    /// 处理"把谷歌浏览器拖走，位置少一个"的场景。
    /// </summary>
    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        PlateBorder.BorderBrush = (Brush)FindResource("HairlineBrush");
    }

    private void StartFileDrag(FrameworkElement source, string target)
    {
        if (!File.Exists(target) && !Directory.Exists(target)) return; // URL 等无文件可拖
        _oleDragging = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { target });
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        finally
        {
            _oleDragging = false;
        }
    }

    // ---------- 点击 / 菜单 ----------

    private void GroupBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GroupVM group) return;
        _vm.SelectedGroup = group; // F21：单击唤出面板并定位分组
        _showPanel();
    }

    private void Pin_Click(object sender, MouseButtonEventArgs e)
    {
        if (_oleDragging) return;
        if ((sender as FrameworkElement)?.Tag is ItemVM item)
            _vm.LaunchItem(item);
    }

    private void Pin_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if ((sender as FrameworkElement)?.Tag is ItemVM item)
            _vm.LaunchItem(item, asAdmin: true); // v0.6：中键=管理员运行
    }

    private void Pin_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ItemVM item) return;
        e.Handled = true; // 防止冒泡到 Dock 根菜单

        // F7 v0.6 / v1.8：图钉完整编辑菜单
        var menu = new ContextMenu { PlacementTarget = fe, Placement = PlacementMode.Top };

        var open = new MenuItem { Header = "启动", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => _vm.LaunchItem(item);
        menu.Items.Add(open);

        var admin = new MenuItem { Header = "以管理员运行" };
        admin.Click += (_, _) => _vm.LaunchItem(item, asAdmin: true);
        menu.Items.Add(admin);

        menu.Items.Add(new Separator());

        // v1.8：完整编辑能力
        var edit = new MenuItem { Header = "编辑…（名称/参数/工作目录）" };
        edit.Click += (_, _) => ShowEditDialog(item);
        menu.Items.Add(edit);

        var rename = new MenuItem { Header = "重命名…" };
        rename.Click += (_, _) =>
        {
            var name = InputDialog.Show(this, $"重命名「{item.Name}」", item.Name, IsLightTheme());
            if (!string.IsNullOrWhiteSpace(name)) _vm.RenamePin(item, name);
        };
        menu.Items.Add(rename);

        if (File.Exists(item.Model.Target) || Directory.Exists(item.Model.Target))
        {
            var locate = new MenuItem { Header = "打开所在位置" };
            locate.Click += (_, _) => RevealInExplorer(item.Model.Target);
            menu.Items.Add(locate);
        }

        menu.Items.Add(new Separator());

        // v1.8：排序
        var idx = _vm.DockPins.IndexOf(item);
        var moveLeft = new MenuItem { Header = "左移", IsEnabled = idx > 0 };
        moveLeft.Click += (_, _) => _vm.MovePin(item, -1);
        menu.Items.Add(moveLeft);

        var moveRight = new MenuItem { Header = "右移", IsEnabled = idx >= 0 && idx < _vm.DockPins.Count - 1 };
        moveRight.Click += (_, _) => _vm.MovePin(item, 1);
        menu.Items.Add(moveRight);

        var toFront = new MenuItem { Header = "移到最前", IsEnabled = idx > 0 };
        toFront.Click += (_, _) => _vm.MovePinToFront(item);
        menu.Items.Add(toFront);

        menu.Items.Add(new Separator());

        var unpin = new MenuItem { Header = "取消钉选" };
        unpin.Click += (_, _) => _vm.Unpin(item);
        menu.Items.Add(unpin);

        menu.IsOpen = true;
    }

    private bool IsLightTheme()
        => string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);

    /// <summary>v1.8：编辑钉选条目（名称/参数/工作目录/管理员）。</summary>
    private void ShowEditDialog(ItemVM item)
    {
        var r = EditItemDialog.Show(this, item.Name, item.Model.Target,
            item.Model.Args, item.Model.WorkDir, item.Model.RunAsAdmin, IsLightTheme());
        if (r is null) return;
        item.Name = r.Name;
        _vm.UpdatePin(item, r.Args, r.WorkDir, r.RunAsAdmin);
    }

    /// <summary>F42 v0.8：Dock 空白处右键 → 新建分组 / 设置 / 置顶开关。</summary>
    private void Root_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Top };

        var newGroup = new MenuItem { Header = "新建分组…", FontWeight = FontWeights.SemiBold };
        newGroup.Click += (_, _) =>
        {
            var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
            var name = InputDialog.Show(this, "新分组名称（如：网安 / 游戏 / 剪辑）", "", light);
            if (!string.IsNullOrWhiteSpace(name))
                _vm.AddGroup(name);
        };
        menu.Items.Add(newGroup);

        // v1.12：Dock 上直接添加钉选（不用先去桌面拖）
        var addPin = new MenuItem { Header = "添加应用/文件到 Dock…" };
        addPin.Click += (_, _) =>
        {
            var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
            var r = AddItemDialog.Show(this, light);
            if (r is { } item)
            {
                if (!_vm.PinToDock(new[] { item.Target }))
                    MessageBox.Show("该目标已在 Dock 中。", "QuickPanel",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        menu.Items.Add(addPin);

        var settings = new MenuItem { Header = "设置…" };
        settings.Click += (_, _) => _openSettings();
        menu.Items.Add(settings);

        var top = new MenuItem
        {
            Header = "Dock 置顶显示",
            IsChecked = _vm.Store.Config.Settings.DockTopmost,
        };
        top.Click += (_, _) =>
        {
            _vm.Store.Config.Settings.DockTopmost = !_vm.Store.Config.Settings.DockTopmost;
            _vm.SaveConfig();
            ApplyTopmost();
        };
        menu.Items.Add(top);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
            else if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch { /* 打开失败静默 */ }
    }

    private void Settings_Click(object sender, MouseButtonEventArgs e) => _openSettings();

    // ---------- 拖入钉选（F22）----------

    private void Dock_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        PlateBorder.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    private void Dock_DragLeave(object sender, DragEventArgs e)
        => PlateBorder.BorderBrush = (Brush)FindResource("HairlineBrush");

    private void Dock_Drop(object sender, DragEventArgs e)
    {
        Dock_DragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _vm.PinToDock(files);
    }
}
