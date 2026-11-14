using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPanel.Models;
using QuickPanel.Services;
using QuickPanel.ViewModels;

namespace QuickPanel.Windows;

/// <summary>F42：卡片式设置。热键 / 自启 / 全屏隐藏 / 热区与收回延迟 / 外观 / Dock（含底板）/ 面板缩放 / 分组 / 数据；深/浅主题。</summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action<string> _hotkeyChanged;
    private readonly Action<double> _opacityChanged;
    private readonly Action _appearanceChanged;
    private readonly Action _layoutChanged;
    private bool _initializing = true;
    private bool _isLight;

    public SettingsWindow(MainViewModel vm,
        Action<string> hotkeyChanged,
        Action<double> opacityChanged,
        Action appearanceChanged,
        Action layoutChanged)
    {
        InitializeComponent();
        _vm = vm;
        _hotkeyChanged = hotkeyChanged;
        _opacityChanged = opacityChanged;
        _appearanceChanged = appearanceChanged;
        _layoutChanged = layoutChanged;

        var settings = vm.Store.Config.Settings;

        // v1.29：混合 DPI 多屏下 WPF 的 CenterScreen 会算到屏幕外（实测 Left=-98），
        // 改为按鼠标所在显示器的工作区手动居中。
        // v1.36：恢复上次调整的窗口尺寸（限制在屏幕内）
        WindowStartupLocation = WindowStartupLocation.Manual;
        var wa = ScreenHelper.GetWorkAreaAtCursor();
        if (settings.SettingsWidth >= MinWidth && settings.SettingsHeight >= MinHeight)
        {
            Width = Math.Min(settings.SettingsWidth, wa.Width);
            Height = Math.Min(settings.SettingsHeight, wa.Height);
        }
        Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
        Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);

        // 拖动边缘/右下角改尺寸 → 记住（RestoreBounds 才是窗口的真实外框）
        SizeChanged += (_, _) =>
        {
            if (WindowState != WindowState.Normal) return;
            if (ActualWidth < MinWidth || ActualHeight < MinHeight) return;
            _vm.Store.Config.Settings.SettingsWidth = ActualWidth;
            _vm.Store.Config.Settings.SettingsHeight = ActualHeight;
        };

        // 版本号取自程序集，不再写死（v1.1 修正：此前硬编码导致显示落后）
        // v1.11：版本号显示改为 "版本 1.10.0"，避免 "v1.10" 被误读为 "v1.1"
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = ver == null ? "版本 ?" : $"版本 {ver.Major}.{ver.Minor}.{ver.Build}";

        HotkeyBox.Text = settings.Hotkey;
        AutostartCheck.IsChecked = Autostart.IsEnabled();
        FullscreenHideCheck.IsChecked = settings.DockAutoHide;
        HotZoneSlider.Value = Math.Clamp(settings.HotZoneMs, 100, 1000);
        HotZoneText.Text = $"{Math.Clamp(settings.HotZoneMs, 100, 1000)} ms";
        HideDelaySlider.Value = Math.Clamp(settings.PanelHideMs, 0, 2000);
        HideDelayText.Text = HideDelaySlider.Value <= 0 ? "立即" : $"{(int)HideDelaySlider.Value} ms";
        StayOpenCheck.IsChecked = settings.PanelStayOpen;
        ThemeDarkRadio.IsChecked = !string.Equals(settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        ThemeLightRadio.IsChecked = string.Equals(settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        ModeAcrylicRadio.IsChecked = string.Equals(settings.BackgroundMode, "acrylic", StringComparison.OrdinalIgnoreCase);
        ModeTransparentRadio.IsChecked = string.Equals(settings.BackgroundMode, "transparent", StringComparison.OrdinalIgnoreCase);
        ModeInvisibleRadio.IsChecked = string.Equals(settings.BackgroundMode, "invisible", StringComparison.OrdinalIgnoreCase);
        SurfaceColorBox.Text = settings.SurfaceColor;
        AccentColorBox.Text = settings.AccentColor;
        PanelSurfaceColorBox.Text = settings.PanelSurfaceColor;
        PanelOpacitySlider.Value = Math.Clamp(settings.PanelOpacity, 0.05, 1.0);
        PanelOpacityText.Text = $"{Math.Clamp(settings.PanelOpacity, 0.05, 1.0) * 100:0}%";
        OpacitySlider.Value = settings.Opacity;
        OpacityValueText.Text = $"{Math.Round(settings.Opacity, 2) * 100:0}%";
        PosCenterRadio.IsChecked = settings.DockPositionMode != "free";
        PosFreeRadio.IsChecked = settings.DockPositionMode == "free";
        DockTopmostCheck.IsChecked = settings.DockTopmost;
        PlateRadio.IsChecked = settings.DockIconPlate;
        BareRadio.IsChecked = !settings.DockIconPlate;
        ShowClockCheck.IsChecked = settings.ShowClock;
        ClockSecondsCheck.IsChecked = settings.ClockShowSeconds;
        ShowMonitorCheck.IsChecked = settings.ShowSystemMonitor;
        WidgetScaleSlider.Value = Math.Clamp(settings.WidgetScale, 0.7, 1.6);
        WidgetScaleText.Text = $"{Math.Clamp(settings.WidgetScale, 0.7, 1.6) * 100:0}%";
        // v1.12：样式细调初值
        PinSizeSlider.Value = Math.Clamp(settings.PinSize, 24, 64);
        PinSizeText.Text = $"{PinSizeSlider.Value:0}";
        PinSpacingSlider.Value = Math.Clamp(settings.PinSpacing, 0, 16);
        PinSpacingText.Text = $"{PinSpacingSlider.Value:0}";
        PinRadiusSlider.Value = Math.Clamp(settings.PinCornerRadius, 0, 22);
        PinRadiusText.Text = $"{PinRadiusSlider.Value:0}";
        HoverScaleSlider.Value = Math.Clamp(settings.HoverScale, 1.0, 1.8);
        HoverScaleText.Text = $"{HoverScaleSlider.Value:0.00}x";
        HoverLiftSlider.Value = Math.Clamp(settings.HoverLift, 0, 20);
        HoverLiftText.Text = $"{HoverLiftSlider.Value:0}";
        ReuseRunningCheck.IsChecked = settings.ReuseRunningWindow;
        DockTextColorBox.Text = settings.DockTextColor;
        DockDesktopOnlyCheck.IsChecked = settings.DockDesktopOnly;
        DockAlwaysVisibleCheck.IsChecked = settings.DockAlwaysVisible;
        UpdateDockOptionEnableState();
        IconScaleSlider.Value = Math.Clamp(settings.IconScale, 0.6, 1.6);
        IconScaleText.Text = $"{Math.Clamp(settings.IconScale, 0.6, 1.6) * 100:0}%";
        PanelScaleSlider.Value = Math.Clamp(settings.PanelScale, 0.8, 1.4);
        PanelScaleText.Text = $"{Math.Clamp(settings.PanelScale, 0.8, 1.4) * 100:0}%";
        if (vm.Store.FirstRun)
        {
            var total = vm.Groups.Sum(g => g.Items.Count);
            FirstRunHint.Text =
                $"首次运行引导：已扫描桌面快捷方式，播种 {total} 个条目到 {vm.Groups.Count} 个分组；也可以在 Dock 上右键「新建分组」。调整后点「完成」开始使用。";
            FirstRunHint.Visibility = Visibility.Visible;
        }

        GroupEditList.ItemsSource = vm.Groups;
        if (vm.Groups.Count > 0) GroupEditList.SelectedIndex = 0;
        // ESC 关闭；标题栏跟随主题 + Win11 圆角
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close();
        };
        // v1.7：无论 Done / Esc / X 关闭，都持久化（此前 Esc/X 会丢失全部改动）
        Closed += (_, _) =>
        {
            try
            {
                _vm.Store.Config.Settings.Opacity = Math.Round(OpacitySlider.Value, 2);
                _vm.Store.Config.Settings.DockAutoHide = FullscreenHideCheck.IsChecked == true;
                _vm.Store.Config.Settings.DockDesktopOnly = DockDesktopOnlyCheck.IsChecked == true;
                _vm.SaveConfig();
            }
            catch { /* 关闭期保存失败不阻塞退出 */ }

            // v1.33：开机自启也在这里兜底（此前只有点「完成」才写，Esc/✕ 会静默丢失）
            try { Autostart.SetEnabled(AutostartCheck.IsChecked == true); }
            catch { /* 无权限等场景静默 */ }
        };
        SourceInitialized += (_, _) =>
            Acrylic.ApplyDialogTheme(new WindowInteropHelper(this).Handle, dark: !_isLight);

        _initializing = false;

        ApplySettingsTheme(settings.SettingsTheme);

        HotZoneSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var ms = (int)Math.Round(HotZoneSlider.Value / 50) * 50;
            HotZoneText.Text = $"{ms} ms";
            settings.HotZoneMs = Math.Clamp(ms, 100, 1000);
        };

        FullscreenHideCheck.Click += (_, _) =>
        {
            if (_initializing) return;
            settings.DockAutoHide = FullscreenHideCheck.IsChecked == true;
        };

        OpacitySlider.ValueChanged += (_, _) =>
        {
            var value = Math.Round(OpacitySlider.Value, 2);
            OpacityValueText.Text = $"{value * 100:0}%";
            if (!_initializing) settings.Opacity = value; // v1.7：写回配置，否则重开面板读旧值
            _opacityChanged(value); // F52：实时预览
        };

        PanelScaleSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var scale = Math.Round(PanelScaleSlider.Value, 2);
            PanelScaleText.Text = $"{scale * 100:0}%";
            settings.PanelScale = Math.Clamp(scale, 0.8, 1.4);
            _layoutChanged();
        };

        // v1.7：图标大小滑块此前完全没有接线（拖动无任何效果）
        IconScaleSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var scale = Math.Round(IconScaleSlider.Value, 2);
            IconScaleText.Text = $"{scale * 100:0}%";
            settings.IconScale = Math.Clamp(scale, 0.6, 1.6);
            _layoutChanged();
        };

        // v1.10：组件字体缩放
        WidgetScaleSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var scale = Math.Round(WidgetScaleSlider.Value, 2);
            WidgetScaleText.Text = $"{scale * 100:0}%";
            settings.WidgetScale = Math.Clamp(scale, 0.7, 1.6);
            _layoutChanged();
        };

        // v1.12：Dock 样式细调
        PinSizeSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var v = Math.Round(PinSizeSlider.Value);
            PinSizeText.Text = $"{v:0}";
            settings.PinSize = Math.Clamp(v, 24, 64);
            _layoutChanged();
        };
        PinSpacingSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var v = Math.Round(PinSpacingSlider.Value);
            PinSpacingText.Text = $"{v:0}";
            settings.PinSpacing = Math.Clamp(v, 0, 16);
            _layoutChanged();
        };
        PinRadiusSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var v = Math.Round(PinRadiusSlider.Value);
            PinRadiusText.Text = $"{v:0}";
            settings.PinCornerRadius = Math.Clamp(v, 0, 22);
            _layoutChanged();
        };
        HoverScaleSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var v = Math.Round(HoverScaleSlider.Value, 2);
            HoverScaleText.Text = $"{v:0.00}x";
            settings.HoverScale = Math.Clamp(v, 1.0, 1.8);
        };
        HoverLiftSlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var v = Math.Round(HoverLiftSlider.Value);
            HoverLiftText.Text = $"{v:0}";
            settings.HoverLift = Math.Clamp(v, 0, 20);
        };

        HideDelaySlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var ms = (int)Math.Round(HideDelaySlider.Value / 100) * 100;
            HideDelayText.Text = ms <= 0 ? "立即" : $"{ms} ms";
            settings.PanelHideMs = Math.Clamp(ms, 0, 2000);
        };

        // v1.21：常驻模式
        StayOpenCheck.Click += (_, _) =>
        {
            if (_initializing) return;
            var on = StayOpenCheck.IsChecked == true;
            settings.PanelStayOpen = on;
            // 常驻时延迟滑杆无意义，置灰
            HideDelaySlider.IsEnabled = !on;
            HideDelayText.Opacity = on ? 0.4 : 1.0;
        };

        SurfaceColorBox.TextChanged += (_, _) => ApplyColor(SurfaceColorBox, isAccent: false);
        AccentColorBox.TextChanged += (_, _) => ApplyColor(AccentColorBox, isAccent: true);
        DockTextColorBox.TextChanged += (_, _) => ApplyDockTextColor();
        PanelSurfaceColorBox.TextChanged += (_, _) => ApplyPanelSurfaceColor();
        PanelOpacitySlider.ValueChanged += (_, _) =>
        {
            var value = Math.Round(PanelOpacitySlider.Value, 2);
            PanelOpacityText.Text = $"{value * 100:0}%";
            if (_initializing) return;
            settings.PanelOpacity = value;
            _appearanceChanged();
        };

        // 初始化时同步滑杆可用性
        HideDelaySlider.IsEnabled = !settings.PanelStayOpen;
        HideDelayText.Opacity = settings.PanelStayOpen ? 0.4 : 1.0;

        GroupEditList.SelectionChanged += (_, _) =>
        {
            if (GroupEditList.SelectedItem is GroupVM group)
            {
                GroupRenameBox.Text = group.Name;
                // v1.14：同步右侧条目列表
                GroupItemsList.ItemsSource = group.Items;
                GroupItemsHeader.Text = $"{group.Name} 的条目（{group.Items.Count}）";
            }
            else
            {
                GroupItemsList.ItemsSource = null;
                GroupItemsHeader.Text = "条目";
            }
        };
    }

    // ---------- 外观 ----------

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.BackgroundMode =
            ModeAcrylicRadio.IsChecked == true ? "acrylic"
            : ModeInvisibleRadio.IsChecked == true ? "invisible"
            : "transparent";
        _appearanceChanged(); // 模式变化 → App 重建窗口
    }

    private void ApplyColor(TextBox box, bool isAccent)
    {
        if (_initializing) return;
        var hex = box.Text.Trim();
        if (hex.Length == 0) return;

        try
        {
            _ = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return;
        }

        if (isAccent) _vm.Store.Config.Settings.AccentColor = hex;
        else _vm.Store.Config.Settings.SurfaceColor = hex;
        _appearanceChanged();
    }

    /// <summary>v1.31：作者仓库入口。</summary>
    private void GitHub_Click(object sender, MouseButtonEventArgs e) => Links.OpenRepo();

    /// <summary>v1.33：开机自启勾选即时生效，失败回弹复选框并提示原因。</summary>
    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var want = AutostartCheck.IsChecked == true;
        try
        {
            if (!Autostart.SetEnabled(want))
            {
                AutostartCheck.IsChecked = Autostart.IsEnabled();
                MessageBox.Show(want
                        ? "无法写入开机自启：程序路径不可用或注册表被拒绝。"
                        : "无法移除开机自启：注册表被拒绝访问。",
                    "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AutostartCheck.IsChecked = Autostart.IsEnabled();
            MessageBox.Show($"开机自启设置失败：{ex.Message}", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split('|');
        if (parts.Length != 2) return;
        if (parts[0] == "surface") SurfaceColorBox.Text = parts[1];
        else if (parts[0] == "accent") AccentColorBox.Text = parts[1];
        else if (parts[0] == "docktext") DockTextColorBox.Text = parts[1];
        else if (parts[0] == "panelsurface") PanelSurfaceColorBox.Text = parts[1];
    }

    /// <summary>v1.29：面板底色实时生效（只接受 #RRGGBB / #AARRGGBB）。</summary>
    private void ApplyPanelSurfaceColor()
    {
        if (_initializing) return;
        var hex = PanelSurfaceColorBox.Text.Trim();
        if (hex.Length == 0) return;
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return;
        }
        _vm.Store.Config.Settings.PanelSurfaceColor = hex;
        _appearanceChanged();
    }

    /// <summary>v1.4：Dock 文字颜色实时生效。</summary>
    private void ApplyDockTextColor()
    {
        if (_initializing) return;
        var hex = DockTextColorBox.Text.Trim();
        if (hex.Length == 0) return;
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return;
        }
        _vm.Store.Config.Settings.DockTextColor = hex;
        _layoutChanged();
    }

    private void DockDesktopOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.DockDesktopOnly = DockDesktopOnlyCheck.IsChecked == true;
    }

    /// <summary>v1.37：打开 Windows 自带的"桌面图标设置"。</summary>
    private void OpenDesktopIconSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:themes",
                UseShellExecute = true,
            });
        }
        catch
        {
            MessageBox.Show("无法打开系统设置。请手动在桌面空白处右键 → 查看 → 取消勾选「显示桌面图标」。",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>v1.31：已运行软件直接切窗口。</summary>
    private void ReuseRunning_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.ReuseRunningWindow = ReuseRunningCheck.IsChecked == true;
    }

    // ---------- 分区导航（v0.9）----------

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 解析期 SelectedIndex="0" 会先于分区控件创建触发，此时直接跳过
        if (NavList is null || SectionTitle is null || SecGeneral is null || SecAppearance is null
            || SecDock is null || SecPanel is null || SecGroups is null || SecData is null)
            return;
        if (NavList.SelectedIndex < 0) return;
        var show = new Action<Grid, bool>((g, v) => g.Visibility = v ? Visibility.Visible : Visibility.Collapsed);
        show(SecGeneral, NavList.SelectedIndex == 0);
        show(SecAppearance, NavList.SelectedIndex == 1);
        show(SecDock, NavList.SelectedIndex == 2);
        show(SecPanel, NavList.SelectedIndex == 3);
        show(SecGroups, NavList.SelectedIndex == 4);
        show(SecData, NavList.SelectedIndex == 5);
        SectionTitle.Text = NavList.SelectedIndex switch
        {
            0 => "常规",
            1 => "外观",
            2 => "Dock",
            3 => "面板",
            4 => "分组",
            _ => "数据",
        };

        // v1.29：切分区回到顶部（内容比窗口高，否则会停在上一页的滚动位置）
        ScrollGeneral?.ScrollToTop();
        ScrollAppearance?.ScrollToTop();
        ScrollDock?.ScrollToTop();
        ScrollPanel?.ScrollToTop();
    }

    // ---------- 设置主题（v0.8）----------

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.SettingsTheme =
            ThemeLightRadio.IsChecked == true ? "light" : "dark";
        ApplySettingsTheme(_vm.Store.Config.Settings.SettingsTheme);
    }

    /// <summary>深/浅主题令牌切换（仅本窗口，v0.9 修正配色；v1.0 标题栏跟随）。</summary>
    private void ApplySettingsTheme(string theme)
    {
        var light = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        _isLight = light;
        var res = Resources;
        res["SbBg"] = Light(light, "#FFF7F7FA", "#FF17171C");
        res["SbNav"] = Light(light, "#FFECEDF4", "#FF121217");
        res["SbCard"] = Light(light, "#FFFFFFFF", "#14FFFFFF");
        res["SbCardBorder"] = Light(light, "#14000000", "#1FFFFFFF");
        res["SbText"] = Light(light, "#FF20242E", "#FFE8EAF0");
        res["SbTextSub"] = Light(light, "#FF6A7080", "#FF9AA0AE");
        res["SbField"] = Light(light, "#FFFFFFFF", "#1FFFFFFF");
        res["SbFieldBorder"] = Light(light, "#22000000", "#3DFFFFFF");
        res["SbBtn"] = Light(light, "#FFF0F1F6", "#2EFFFFFF");
        res["SbBtnBorder"] = Light(light, "#2E000000", "#47FFFFFF");
        res["SbBtnHover"] = Light(light, "#FFE4E6F0", "#42FFFFFF");
        res["SbBtnPress"] = Light(light, "#FFD8DBE6", "#21FFFFFF");
        res["SbSwatchBorder"] = Light(light, "#2E000000", "#59FFFFFF");

        // v1.0：窗口已显示时（用户切主题），标题栏立即跟随
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
            Acrylic.ApplyDialogTheme(new WindowInteropHelper(this).Handle, dark: !light);
    }

    private static SolidColorBrush Light(bool light, string lightHex, string darkHex)
        => new((Color)ColorConverter.ConvertFromString(light ? lightHex : darkHex));

    private void Plate_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.DockIconPlate = PlateRadio.IsChecked == true;
        _layoutChanged();
    }

    /// <summary>v1.22：Dock 常驻开关。</summary>
    private void DockAlwaysVisible_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.DockAlwaysVisible = DockAlwaysVisibleCheck.IsChecked == true;
        UpdateDockOptionEnableState();
    }

    /// <summary>常驻开启时，"仅桌面显示"和"全屏隐藏"失去意义，置灰。</summary>
    private void UpdateDockOptionEnableState()
    {
        var always = DockAlwaysVisibleCheck.IsChecked == true;
        DockDesktopOnlyCheck.IsEnabled = !always;
        DockDesktopOnlyCheck.Opacity = always ? 0.4 : 1.0;
        FullscreenHideCheck.IsEnabled = !always;
        FullscreenHideCheck.Opacity = always ? 0.4 : 1.0;
    }

    /// <summary>v1.21：面板常驻开关。</summary>
    private void StayOpen_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.PanelStayOpen = StayOpenCheck.IsChecked == true;
    }

    /// <summary>v1.9：时钟/监控组件开关。</summary>
    private void Widget_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var s = _vm.Store.Config.Settings;
        s.ShowClock = ShowClockCheck.IsChecked == true;
        s.ClockShowSeconds = ClockSecondsCheck.IsChecked == true;
        s.ShowSystemMonitor = ShowMonitorCheck.IsChecked == true;
        _layoutChanged();
    }
    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        var settings = _vm.Store.Config.Settings;
        settings.BackgroundMode = "transparent";
        settings.SurfaceColor = "#3C151518";
        settings.AccentColor = "#7C6FFF";
        settings.PanelSurfaceColor = "#151518";
        settings.PanelOpacity = 0.94;
        settings.IconScale = 1.0;
        settings.PanelScale = 1.0;
        settings.DockPositionMode = "bottomCenter";
        settings.DockX = 0;
        settings.DockY = 0;
        settings.DockTopmost = false;
        settings.DockIconPlate = true;
        settings.DockTextColor = "#000000";
        settings.DockDesktopOnly = true;
        settings.WidgetScale = 1.0;
        settings.ReuseRunningWindow = true;

        _initializing = true;
        ModeAcrylicRadio.IsChecked = false;
        ModeTransparentRadio.IsChecked = true;
        ModeInvisibleRadio.IsChecked = false;
        SurfaceColorBox.Text = settings.SurfaceColor;
        AccentColorBox.Text = settings.AccentColor;
        PanelSurfaceColorBox.Text = settings.PanelSurfaceColor;
        PanelOpacitySlider.Value = Math.Clamp(settings.PanelOpacity, 0.05, 1.0);
        PanelOpacityText.Text = $"{PanelOpacitySlider.Value * 100:0}%";
        ReuseRunningCheck.IsChecked = settings.ReuseRunningWindow;
        IconScaleSlider.Value = 1.0;
        WidgetScaleSlider.Value = 1.0;
        PanelScaleSlider.Value = 1.0;
        PosCenterRadio.IsChecked = true;
        PosFreeRadio.IsChecked = false;
        DockTopmostCheck.IsChecked = false;
        PlateRadio.IsChecked = true;
        BareRadio.IsChecked = false;
        DockTextColorBox.Text = settings.DockTextColor;
        DockDesktopOnlyCheck.IsChecked = true;
        _initializing = false;

        _appearanceChanged();
        _layoutChanged();
    }

    // ---------- Dock / 面板 ----------

    private void Pos_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.DockPositionMode =
            PosFreeRadio.IsChecked == true ? "free" : "bottomCenter";

        _layoutChanged();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        var settings = _vm.Store.Config.Settings;
        settings.DockPositionMode = "bottomCenter";
        settings.DockX = 0;
        settings.DockY = 0;
        _initializing = true;
        PosCenterRadio.IsChecked = true;
        PosFreeRadio.IsChecked = false;
        _initializing = false;
        _layoutChanged();
    }

    private void DockTopmost_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _vm.Store.Config.Settings.DockTopmost = DockTopmostCheck.IsChecked == true;
        _layoutChanged();
    }

    // ---------- 分组管理 ----------

    // v1.13：分组拖拽排序
    private void GroupEdit_Down(object sender, MouseButtonEventArgs e)
        => GroupDragHelper.OnDown(GroupEditList, e);

    private void GroupEdit_Move(object sender, MouseEventArgs e)
        => GroupDragHelper.TryStartDrag(GroupEditList, e);

    private void GroupEdit_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("QuickPanel.Group")
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupEdit_Drop(object sender, DragEventArgs e)
    {
        GroupDragHelper.OnDrop(GroupEditList, e, (g, idx) => _vm.MoveGroup(g, idx));
        e.Handled = true;
    }

    // ---------- v1.14：分组内条目管理 ----------

    private GroupVM? CurrentGroup => GroupEditList.SelectedItem as GroupVM;

    private void RefreshItemsHeader()
    {
        var g = CurrentGroup;
        GroupItemsHeader.Text = g is null ? "条目" : $"{g.Name} 的条目（{g.Items.Count}）";
    }

    /// <summary>双击条目 → 启动。</summary>
    private void GroupItems_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupItemsList.SelectedItem is ItemVM item)
            _vm.LaunchItem(item);
    }

    /// <summary>从当前分组移除选中条目。</summary>
    private void GroupItems_Remove_Click(object sender, RoutedEventArgs e)
    {
        var g = CurrentGroup;
        if (g is null || GroupItemsList.SelectedItem is not ItemVM item) return;
        g.RemoveItem(item);
        _vm.SaveConfig();
        RefreshItemsHeader();
    }

    /// <summary>把选中条目移动到其它分组。</summary>
    private void GroupItems_MoveTo_Click(object sender, RoutedEventArgs e)
    {
        var g = CurrentGroup;
        if (g is null || GroupItemsList.SelectedItem is not ItemVM item) return;

        var targets = _vm.Groups.Where(x => !ReferenceEquals(x, g)).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("没有其它分组可移动。", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu { PlacementTarget = GroupItemsList, Placement = PlacementMode.Bottom };
        foreach (var t in targets)
        {
            var target = t;
            var mi = new MenuItem { Header = $"{target.Name}（{target.Items.Count} 项）" };
            mi.Click += (_, _) =>
            {
                _vm.MoveItem(item, target);
                RefreshItemsHeader();
            };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    /// <summary>v1.23：扫描桌面/开始菜单，批量添加程序到分组。</summary>
    private void GroupItems_Scan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new ScanAddWindow(_vm) { Owner = this };
            dlg.ShowDialog();
            RefreshItemsHeader();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"扫描失败：{ex.Message}", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>向当前分组添加条目（文件/文件夹/网址）。</summary>
    private void GroupItems_Add_Click(object sender, RoutedEventArgs e)
    {
        var g = CurrentGroup;
        if (g is null) return;
        var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        var r = AddItemDialog.Show(this, light);
        if (r is not { } item) return;

        if (g.Items.Any(i => string.Equals(i.Model.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("该目标已在当前分组中。", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var model = new LaunchItem
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name.Trim(),
            Target = item.Target.Trim(),
            Kind = item.Kind,
            WorkDir = System.IO.File.Exists(item.Target)
                ? (System.IO.Path.GetDirectoryName(item.Target) ?? "") : "",
        };
        g.AddItem(new ItemVM(model, _vm.Icons));
        _vm.SaveConfig();
        RefreshItemsHeader();
    }

    // 条目拖拽排序（v1.14：独立数据格式，与分组拖拽区分）
    private Point _itemDragStart;
    private ItemVM? _itemDragged;

    private void GroupItems_Down(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestorListItem(e.OriginalSource as DependencyObject);
        _itemDragged = container?.DataContext as ItemVM;
        if (_itemDragged != null) _itemDragStart = e.GetPosition(GroupItemsList);
    }

    private void GroupItems_Move(object sender, MouseEventArgs e)
    {
        if (_itemDragged is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(GroupItemsList);
        if (Math.Abs(pos.X - _itemDragStart.X) + Math.Abs(pos.Y - _itemDragStart.Y)
            < SystemParameters.MinimumVerticalDragDistance) return;

        var dragged = _itemDragged;
        _itemDragged = null;
        try
        {
            var data = new DataObject("QuickPanel.Item", dragged);
            DragDrop.DoDragDrop(GroupItemsList, data, DragDropEffects.Move);
        }
        catch { /* 拖拽取消 */ }
    }

    private void GroupItems_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("QuickPanel.Item")
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupItems_Drop(object sender, DragEventArgs e)
    {
        var g = CurrentGroup;
        if (g is null) return;
        if (e.Data.GetData("QuickPanel.Item") is not ItemVM dragged) return;

        var target = FindAncestorListItem(e.OriginalSource as DependencyObject);
        if (target?.DataContext is not ItemVM targetItem) return;

        var from = g.Items.IndexOf(dragged);
        var to = g.Items.IndexOf(targetItem);
        if (from < 0 || to < 0 || from == to) return;

        var pos = e.GetPosition(target);
        if (pos.Y > target.ActualHeight / 2 && to < g.Items.Count - 1) to++;

        _vm.MoveItemWithinGroup(g, dragged, to);
        e.Handled = true;
    }

    private static ListBoxItem? FindAncestorListItem(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is ListBoxItem hit) return hit;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void Group_Add_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddGroup($"新分组{_vm.Groups.Count + 1}");
        GroupEditList.SelectedIndex = _vm.Groups.Count - 1;
    }

    private void Group_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (GroupEditList.SelectedItem is not GroupVM group) return;
        var name = GroupRenameBox.Text.Trim();
        if (name.Length == 0 || name == group.Name) return;
        group.Name = name;
        _vm.SaveConfig();
    }

    private void Group_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (GroupEditList.SelectedItem is not GroupVM group) return;
        if (_vm.Groups.Count <= 1)
        {
            MessageBox.Show("至少保留一个分组。", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"删除分组「{group.Name}」及其 {group.Items.Count} 个条目？",
            "QuickPanel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _vm.RemoveGroup(group);
        if (_vm.Groups.Count > 0) GroupEditList.SelectedIndex = 0;
    }

    // ---------- 数据 ----------

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _vm.Store.Backup();
            MessageBox.Show($"已备份到：\n{path}", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"备份失败：{ex.Message}", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenConfigDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ConfigStore.DataDir, UseShellExecute = true });
        }
        catch { /* 打开失败静默 */ }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        var hotkey = HotkeyBox.Text.Trim();
        if (hotkey.Length > 0 &&
            !string.Equals(hotkey, _vm.Store.Config.Settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            _vm.Store.Config.Settings.Hotkey = hotkey;
            _hotkeyChanged(hotkey);
        }

        _vm.Store.Config.Settings.Opacity = Math.Round(OpacitySlider.Value, 2);
        _vm.Store.Config.Settings.DockAutoHide = FullscreenHideCheck.IsChecked == true;

        try
        {
            Autostart.SetEnabled(AutostartCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"开机自启设置失败：{ex.Message}", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _vm.SaveConfig();
        _opacityChanged(_vm.Store.Config.Settings.Opacity);
        Close();
    }
}
