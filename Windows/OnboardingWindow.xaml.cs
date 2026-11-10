using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPanel.Services;
using QuickPanel.ViewModels;

namespace QuickPanel.Windows;

/// <summary>
/// v1.23：首次运行初始化向导 —— 欢迎 → 扫描勾选 → 设置热键 → 完成。
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action<string> _applyHotkey;
    private readonly Action _applyAppearance;
    private int _step; // 0=欢迎 1=整理 2=热键 3=外观 4=完成
    private List<ScannedApp> _scanned = new();
    private readonly List<CheckBox> _checkBoxes = new();
    private bool _appearanceReady;

    public OnboardingWindow(MainViewModel vm, Action<string> applyHotkey, Action applyAppearance)
    {
        InitializeComponent();
        _vm = vm;
        _applyHotkey = applyHotkey;
        _applyAppearance = applyAppearance;

        // 圆角 + 深色标题栏（无边框，但仍调用一次统一主题）
        SourceInitialized += (_, _) =>
        {
            try { Acrylic.ApplyDialogTheme(new WindowInteropHelper(this).Handle, dark: true); }
            catch { /* 忽略 */ }
        };

        // 支持拖动窗口（无边框）—— 但必须避开可交互控件，否则 DragMove 会吞掉按钮点击
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { /* 忽略 */ }
        };

        // Esc 等同于跳过（首运时）
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Skip_Click(this, new RoutedEventArgs()); e.Handled = true; }
        };

        HotkeyBox.Text = vm.Store.Config.Settings.Hotkey;
        BuildAccentPalette();
        InitAppearanceSelection();
        ShowStep(0);

        // v1.24：固定在主屏居中显示 —— 不跟随鼠标所在的副屏，避免用户在主屏看不到向导
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    // ---------- v1.24：外观 ----------

    private void BuildAccentPalette()
    {
        var colors = new[] { "#7C6FFF", "#FF7C6F", "#4FC3F7", "#66BB6A", "#FFD54F", "#E066FF" };
        foreach (var hex in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                Tag = hex,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
            };
            swatch.MouseLeftButtonUp += (_, _) => SelectAccent(hex);
            AccentPalette.Children.Add(swatch);
        }
    }

    private void SelectAccent(string hex)
    {
        foreach (var child in AccentPalette.Children.OfType<Border>())
        {
            var isMatch = string.Equals(child.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
            child.BorderBrush = isMatch ? Brushes.White : Brushes.Transparent;
        }
        _vm.Store.Config.Settings.AccentColor = hex;
        try { _applyAppearance(); } catch { /* 应用失败不影响 */ }
    }

    private void InitAppearanceSelection()
    {
        _appearanceReady = false;
        var mode = _vm.Store.Config.Settings.BackgroundMode;
        AcrylicRadio.IsChecked = string.Equals(mode, "acrylic", StringComparison.OrdinalIgnoreCase);
        InvisibleRadio.IsChecked = string.Equals(mode, "invisible", StringComparison.OrdinalIgnoreCase);
        TransparentRadio.IsChecked = AcrylicRadio.IsChecked != true && InvisibleRadio.IsChecked != true;
        // 默认选中当前强调色
        SelectAccent(_vm.Store.Config.Settings.AccentColor);
        _appearanceReady = true;
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_appearanceReady) return;
        _vm.Store.Config.Settings.BackgroundMode =
            AcrylicRadio.IsChecked == true ? "acrylic"
            : InvisibleRadio.IsChecked == true ? "invisible"
            : "transparent";
        try { _applyAppearance(); } catch { /* 忽略 */ }
    }

    private void IncludeStartMenu_Changed(object sender, RoutedEventArgs e)
    {
        StartScan();
    }

    // ---------- 步骤切换 ----------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 4);

        PageWelcome.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageScan.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageHotkey.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageDone.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;

        // 标题
        (TitleText.Text, SubtitleText.Text) = _step switch
        {
            0 => ("欢迎使用 QuickPanel", "几步完成配置，马上开始使用"),
            1 => ("整理桌面上的程序", "勾选要放上面板的，会自动分好类"),
            2 => ("设置呼出快捷键", "任何时候按下都能呼出面板"),
            3 => ("选一个你喜欢的样子", "改完立即生效，之后也能随时调整"),
            _ => ("全部就绪", "开始使用你的启动面板"),
        };

        // 步骤点
        var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        for (var i = 0; i < dots.Length; i++)
            dots[i].Fill = new SolidColorBrush(i <= _step
                ? Color.FromRgb(0x7C, 0x6F, 0xFF)
                : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

        // 按钮
        BackBtn.Visibility = _step is > 0 and < 4 ? Visibility.Visible : Visibility.Collapsed;
        SkipBtn.Visibility = _step is > 0 and < 4 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = _step switch
        {
            0 => "开始设置",
            1 => "下一步",
            2 => "下一步",
            3 => "下一步",
            _ => "开始使用",
        };

        if (_step == 1) StartScan();
        if (_step == 3) InitAppearanceSelection();
        if (_step == 4) UpdateDoneSummary();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1) ApplySelectedApps();

        if (_step == 2)
        {
            // 校验热键
            var text = HotkeyBox.Text.Trim();
            if (!HotkeyService.TryParse(text, out _, out _))
            {
                HotkeyError.Text = "格式无法识别。示例：Alt+Q、Ctrl+Alt+Space、Shift+F1";
                HotkeyError.Visibility = Visibility.Visible;
                return;
            }
            HotkeyError.Visibility = Visibility.Collapsed;
            _vm.Store.Config.Settings.Hotkey = text;
            _applyHotkey(text);   // 立即注册（失败会自动回退并提示）
            HotkeyBox.Text = _vm.Store.Config.Settings.Hotkey;
        }

        if (_step == 4) { Finish(); return; }

        ShowStep(_step + 1);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // 跳过：用默认值直接完成（首运时执行一次默认扫描）
        ApplyDefaultSeedIfEmpty();
        Finish();
    }

    // ---------- 扫描 ----------

    private void StartScan()
    {
        ScanSummaryText.Text = "正在整理…";
        ScanListHost.Children.Clear();
        _checkBoxes.Clear();

        try
        {
            _scanned = AppScanner.Scan(includeStartMenu: IncludeStartMenuCheck.IsChecked == true);
        }
        catch
        {
            _scanned = new List<ScannedApp>();
        }

        if (_scanned.Count == 0)
        {
            ScanSummaryText.Text = IncludeStartMenuCheck.IsChecked == true
                ? "没有找到程序。你可以稍后把文件拖到面板里添加。"
                : "桌面上没有快捷方式。勾选上面的选项可以整理开始菜单里的程序。";
            return;
        }

        var selected = _scanned.Count(a => a.IsSelected);
        ScanSummaryText.Text = $"找到 {_scanned.Count} 个程序，已为你勾选 {selected} 个";
        RenderList(_scanned);
    }

    /// <summary>按分组渲染勾选列表（可被筛选重用）。</summary>
    private void RenderList(List<ScannedApp> apps)
    {
        ScanListHost.Children.Clear();
        _checkBoxes.Clear();

        var first = true;
        foreach (var groupId in new[] { "dev", "office", "media", "game", "social", "tools" })
        {
            var groupApps = apps.Where(a => a.GroupId == groupId).ToList();
            if (groupApps.Count == 0) continue;

            ScanListHost.Children.Add(new TextBlock
            {
                Text = $"{AppScanner.GroupDisplayName(groupId)}  ·  {groupApps.Count} 个",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x6F, 0xFF)),
                Margin = new Thickness(0, first ? 0 : 16, 0, 7),
            });
            first = false;

            var wrap = new WrapPanel();
            foreach (var app in groupApps)
            {
                var cb = new CheckBox
                {
                    Content = app.Item.Name,
                    IsChecked = app.IsSelected,
                    Tag = app,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDD, 0xE2)),
                    Margin = new Thickness(0, 3, 18, 3),
                    Width = 186,
                    ToolTip = $"{app.Item.Name}（来源：{app.Source}）",
                    Cursor = Cursors.Hand,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                cb.Checked += (_, _) => { if (cb.Tag is ScannedApp a) a.IsSelected = true; };
                cb.Unchecked += (_, _) => { if (cb.Tag is ScannedApp a) a.IsSelected = false; };
                _checkBoxes.Add(cb);
                wrap.Children.Add(cb);
            }
            ScanListHost.Children.Add(wrap);
        }

        if (ScanListHost.Children.Count == 0)
        {
            ScanListHost.Children.Add(new TextBlock
            {
                Text = "没有匹配的程序",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAE)),
            });
        }
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterHint.Visibility = FilterBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        var q = FilterBox.Text.Trim();
        if (q.Length == 0) { RenderList(_scanned); return; }
        var filtered = _scanned.Where(a =>
            a.Item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        RenderList(filtered);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _checkBoxes) cb.IsChecked = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _checkBoxes) cb.IsChecked = false;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => StartScan();

    /// <summary>把勾选的程序写入分组与 Dock 钉选。</summary>
    private void ApplySelectedApps()
    {
        var selected = _scanned.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ApplyDefaultSeedIfEmpty(); // 一个都没选 → 保持分组结构，留空
            return;
        }

        // 确保六个分组存在
        foreach (var (id, name) in StandardGroups)
        {
            if (_vm.Groups.All(g => g.Id != id))
                _vm.AddGroupInternal(id, name);
        }

        foreach (var app in selected)
        {
            var group = _vm.Groups.FirstOrDefault(g => g.Id == app.GroupId)
                        ?? _vm.Groups.FirstOrDefault();
            if (group is null) continue;
            group.AddItem(new ItemVM(app.Item, _vm.Icons));
        }

        // Dock 钉选：取前 6 个（优先常用关键词命中的）
        SeedDockPins(_vm, selected.Select(a => a.Item).ToList());
        _vm.SaveConfig();
    }

    private static void SeedDockPins(MainViewModel vm, List<Models.LaunchItem> items)
    {
        if (vm.DockPins.Count > 0) return; // 已有钉选不覆盖
        var keywords = new[]
        {
            "wechat", "weixin", "微信", "chrome", "edge", "code", "vscode",
            "terminal", "everything", "explorer", "文件资源管理器", "qq",
        };
        var picked = new List<Models.LaunchItem>();
        foreach (var kw in keywords)
        {
            var hit = items.FirstOrDefault(i =>
                i.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                && picked.All(p => p.Target != i.Target));
            if (hit is null) continue;
            picked.Add(hit);
            if (picked.Count >= 6) break;
        }
        // 不足 6 个时用列表前几项补齐
        foreach (var it in items)
        {
            if (picked.Count >= 6) break;
            if (picked.Any(p => p.Target == it.Target)) continue;
            picked.Add(it);
        }
        foreach (var it in picked)
            vm.PinItemDirect(it);
    }

    /// <summary>用户跳过或没选任何程序时：保证有标准分组可用。</summary>
    private void ApplyDefaultSeedIfEmpty()
    {
        if (_vm.Groups.Count > 0) return;
        foreach (var (id, name) in StandardGroups)
            _vm.AddGroupInternal(id, name);
        _vm.SaveConfig();
    }

    /// <summary>v1.24：标准分组（通用 6 类，不含特定人群类目）。</summary>
    private static readonly (string Id, string Name)[] StandardGroups =
    {
        ("dev", "开发"), ("office", "办公"), ("media", "影音"),
        ("game", "游戏"), ("social", "社交"), ("tools", "工具"),
    };

    private void UpdateDoneSummary()
    {
        var total = _vm.Groups.Sum(g => g.Items.Count);
        DoneSummaryText.Text =
            $"已把 {total} 个程序整理到 {_vm.Groups.Count} 个分组，Dock 上放了 {_vm.DockPins.Count} 个常用。\n" +
            "之后随时可以在设置里继续添加或调整。";
        DoneHotkeyRun.Text = _vm.Store.Config.Settings.Hotkey;
    }

    private void Finish()
    {
        try { _vm.SaveConfig(); } catch { /* 忽略 */ }
        DialogResult = true;
        Close();
    }

    /// <summary>v1.23：判断点击源是否落在可交互控件上（按钮/输入框/勾选框/下拉框）。</summary>
    private static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBoxBase or ComboBox or ScrollBar) return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
