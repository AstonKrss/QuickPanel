using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

        // 版本号取自程序集，不再写死（v1.1 修正：此前硬编码导致显示落后）
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

        HotkeyBox.Text = settings.Hotkey;
        AutostartCheck.IsChecked = Autostart.IsEnabled();
        FullscreenHideCheck.IsChecked = settings.DockAutoHide;
        HotZoneSlider.Value = Math.Clamp(settings.HotZoneMs, 100, 1000);
        HotZoneText.Text = $"{Math.Clamp(settings.HotZoneMs, 100, 1000)} ms";
        HideDelaySlider.Value = Math.Clamp(settings.PanelHideMs, 0, 2000);
        HideDelayText.Text = HideDelaySlider.Value <= 0 ? "立即" : $"{(int)HideDelaySlider.Value} ms";
        ThemeDarkRadio.IsChecked = !string.Equals(settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        ThemeLightRadio.IsChecked = string.Equals(settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        ModeAcrylicRadio.IsChecked = string.Equals(settings.BackgroundMode, "acrylic", StringComparison.OrdinalIgnoreCase);
        ModeTransparentRadio.IsChecked = string.Equals(settings.BackgroundMode, "transparent", StringComparison.OrdinalIgnoreCase);
        ModeInvisibleRadio.IsChecked = string.Equals(settings.BackgroundMode, "invisible", StringComparison.OrdinalIgnoreCase);
        SurfaceColorBox.Text = settings.SurfaceColor;
        AccentColorBox.Text = settings.AccentColor;
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
        DockTextColorBox.Text = settings.DockTextColor;
        DockDesktopOnlyCheck.IsChecked = settings.DockDesktopOnly;
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

        HideDelaySlider.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            var ms = (int)Math.Round(HideDelaySlider.Value / 100) * 100;
            HideDelayText.Text = ms <= 0 ? "立即" : $"{ms} ms";
            settings.PanelHideMs = Math.Clamp(ms, 0, 2000);
        };

        SurfaceColorBox.TextChanged += (_, _) => ApplyColor(SurfaceColorBox, isAccent: false);
        AccentColorBox.TextChanged += (_, _) => ApplyColor(AccentColorBox, isAccent: true);
        DockTextColorBox.TextChanged += (_, _) => ApplyDockTextColor();

        GroupEditList.SelectionChanged += (_, _) =>
        {
            if (GroupEditList.SelectedItem is GroupVM group)
                GroupRenameBox.Text = group.Name;
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

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split('|');
        if (parts.Length != 2) return;
        if (parts[0] == "surface") SurfaceColorBox.Text = parts[1];
        else if (parts[0] == "accent") AccentColorBox.Text = parts[1];
        else if (parts[0] == "docktext") DockTextColorBox.Text = parts[1];
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
        settings.IconScale = 1.0;
        settings.PanelScale = 1.0;
        settings.DockPositionMode = "bottomCenter";
        settings.DockX = 0;
        settings.DockY = 0;
        settings.DockTopmost = false;
        settings.DockIconPlate = true;
        settings.DockTextColor = "#000000";
        settings.DockDesktopOnly = true;

        _initializing = true;
        ModeAcrylicRadio.IsChecked = false;
        ModeTransparentRadio.IsChecked = true;
        ModeInvisibleRadio.IsChecked = false;
        SurfaceColorBox.Text = settings.SurfaceColor;
        AccentColorBox.Text = settings.AccentColor;
        IconScaleSlider.Value = 1.0;
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
