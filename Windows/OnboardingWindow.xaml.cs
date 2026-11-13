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

/// <summary>
/// v1.25：首次运行初始化向导。
/// 交互改为"用户自己组织"：左侧列出扫描到的程序，右侧由用户新建分组并拖入；
/// 另有「常用」区对应 Dock 钉选。不做自动分类。
/// </summary>
public partial class OnboardingWindow : Window
{
    private const string DragFormat = "QuickPanel.OnboardApp";

    private readonly MainViewModel _vm;
    private readonly Action<string> _applyHotkey;
    private readonly Action _applyAppearance;
    private int _step; // 0=欢迎 1=整理 2=选项 3=外观 4=完成

    private List<ScannedApp> _all = new();
    private readonly List<ScannedApp> _unassigned = new();
    private bool _appearanceReady;

    private Point _dragStart;
    private ScannedApp? _dragCandidate;
    private ScannedApp? _zoneDragCandidate;

    public OnboardingWindow(MainViewModel vm, Action<string> applyHotkey, Action applyAppearance)
    {
        InitializeComponent();
        _vm = vm;
        _applyHotkey = applyHotkey;
        _applyAppearance = applyAppearance;

        SourceInitialized += (_, _) =>
        {
            try { Acrylic.ApplyDialogTheme(new WindowInteropHelper(this).Handle, dark: true); }
            catch { /* 忽略 */ }
        };

        // 无边框拖动 —— 必须避开可交互控件，否则 DragMove 会吞掉点击
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { /* 忽略 */ }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Skip_Click(this, new RoutedEventArgs()); e.Handled = true; }
        };

        HotkeyBox.Text = vm.Store.Config.Settings.Hotkey;
        AutostartCheck.IsChecked = Autostart.IsEnabled();
        DockAlwaysCheck.IsChecked = vm.Store.Config.Settings.DockAlwaysVisible;
        BuildAccentPalette();
        BuildTextColorPalette();
        InitAppearanceSelection();
        ShowStep(0);

        // v1.26：左侧未整理区也是落点 —— 拖回来即可"改错"
        UnassignedZone.DragOver += (_, e) =>
        {
            var ok = e.Data.GetDataPresent(DragFormat);
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            UnassignedZone.Background = new SolidColorBrush(ok
                ? Color.FromArgb(0x30, 0x7C, 0x6F, 0xFF)
                : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            e.Handled = true;
        };
        UnassignedZone.DragLeave += (_, _) =>
            UnassignedZone.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        UnassignedZone.Drop += (_, e) =>
        {
            UnassignedZone.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            if (e.Data.GetData(DragFormat) is ScannedApp app) ReturnToUnassigned(app);
            e.Handled = true;
        };

        // 固定在主屏居中显示
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    // ---------- 步骤 ----------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 4);

        PageWelcome.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageOrganize.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageOptions.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageDone.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;

        (TitleText.Text, SubtitleText.Text) = _step switch
        {
            0 => ("欢迎使用 QuickPanel", "几步完成配置，马上开始使用"),
            1 => ("整理桌面上的程序", "拖到右边的分组里；没有合适的分组就自己建一个"),
            2 => ("呼出方式与启动", "设一个好记的快捷键"),
            3 => ("选一个你喜欢的样子", "改完立即生效，之后也能随时调整"),
            _ => ("全部就绪", "开始使用你的启动面板"),
        };

        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };
        for (var i = 0; i < dots.Length; i++)
            dots[i].Fill = new SolidColorBrush(i <= _step
                ? Color.FromRgb(0x7C, 0x6F, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00));

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
        if (_step == 2)
        {
            var text = HotkeyBox.Text.Trim();
            if (!HotkeyService.TryParse(text, out _, out _))
            {
                HotkeyError.Text = "格式无法识别。示例：Alt+Q、Ctrl+Alt+Space、Shift+F1";
                HotkeyError.Visibility = Visibility.Visible;
                return;
            }
            HotkeyError.Visibility = Visibility.Collapsed;
            _vm.Store.Config.Settings.Hotkey = text;
            _applyHotkey(text);
            HotkeyBox.Text = _vm.Store.Config.Settings.Hotkey;

            // 开机自启
            try { Autostart.SetEnabled(AutostartCheck.IsChecked == true); }
            catch { /* 写注册表失败不影响流程 */ }

            // Dock 是否常驻
            _vm.Store.Config.Settings.DockAlwaysVisible = DockAlwaysCheck.IsChecked == true;

        }

        if (_step == 4) { Finish(); return; }
        ShowStep(_step + 1);
    }

    /// <summary>v1.31：作者链接（只在用户点击时打开浏览器）。</summary>
    private void GitHubLink_Click(object sender, RoutedEventArgs e) => Links.OpenRepo();

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        try { _vm.SaveConfig(); } catch { /* 忽略 */ }
        DialogResult = true;
        Close();
    }

    // ---------- 扫描与图标网格 ----------

    private void StartScan(bool includeStartMenu = false)
    {
        try { _all = AppScanner.Scan(includeStartMenu); }
        catch { _all = new List<ScannedApp>(); }

        // 未整理的 = 全量（已加入分组的在加入时从列表移除）
        _unassigned.Clear();
        _unassigned.AddRange(_all);
        RefreshUnassigned();
        RefreshGroupZones();
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => StartScan();

    private void AddStartMenu_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        if (string.Equals(btn.Content as string, "加开始菜单"))
        {
            StartScan(includeStartMenu: true);
            btn.Content = "去掉开始菜单";
        }
        else
        {
            StartScan(includeStartMenu: false);
            btn.Content = "加开始菜单";
        }
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterHint.Visibility = FilterBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshUnassigned();
    }

    /// <summary>渲染左侧未整理图标网格。</summary>
    private void RefreshUnassigned()
    {
        var q = FilterBox.Text.Trim();
        var list = q.Length == 0
            ? _unassigned
            : _unassigned.Where(a => a.Item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        UnassignedHeader.Text = q.Length == 0
            ? $"桌面上的程序（{_unassigned.Count}）"
            : $"匹配「{q}」（{list.Count} / {_unassigned.Count}）";

        UnassignedHost.ItemsSource = list.Select(a => CreateTile(a)).ToList();

        EmptyHint.Visibility = _unassigned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _all.Count == 0
            ? "没有找到程序。你可以稍后把文件直接拖到面板里添加。"
            : "都整理好了。点「下一步」继续。";
    }

    /// <summary>创建一个可拖拽的程序磁贴。</summary>
    private Border CreateTile(ScannedApp app)
    {
        // v1.25：图标只解析一次，重绘时复用
        app.Icon ??= _vm.Icons.GetIcon(app.Item);
        var img = new Image
        {
            Source = app.Icon,
            Width = 34,
            Height = 34,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var name = new TextBlock
        {
            Text = app.Item.Name,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 26,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(img);
        stack.Children.Add(name);

        var tile = new Border
        {
            Style = (Style)FindResource("AppTile"),
            Child = stack,
            Tag = app,
            ToolTip = $"{app.Item.Name}\n拖到右边分组，或右键选择",
        };

        tile.MouseLeftButtonDown += Tile_MouseDown;
        tile.MouseMove += Tile_MouseMove;
        tile.MouseRightButtonUp += Tile_RightClick;
        return tile;
    }

    private void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: ScannedApp app })
        {
            _dragCandidate = app;
            _dragStart = e.GetPosition(this);
        }
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) + Math.Abs(pos.Y - _dragStart.Y)
            < SystemParameters.MinimumHorizontalDragDistance) return;

        var app = _dragCandidate;
        _dragCandidate = null;
        try
        {
            var data = new DataObject(DragFormat, app);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        catch { /* 拖拽取消 */ }
    }

    /// <summary>右键：加入分组 / 加入常用 / 忽略。</summary>
    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ScannedApp app }) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };

        var addCommon = new MenuItem { Header = "加入常用（Dock）" };
        addCommon.Click += (_, _) => AddToDock(app);
        menu.Items.Add(addCommon);

        if (_vm.Groups.Count > 0)
        {
            var toGroup = new MenuItem { Header = "加入分组" };
            foreach (var g in _vm.Groups)
            {
                var target = g;
                var mi = new MenuItem { Header = g.Name };
                mi.Click += (_, _) => AddToGroup(app, target);
                toGroup.Items.Add(mi);
            }
            menu.Items.Add(toGroup);
        }
        else
        {
            var hint = new MenuItem { Header = "（还没有分组，先点右上角新建）", IsEnabled = false };
            menu.Items.Add(hint);
        }

        var ignore = new MenuItem { Header = "不加入，暂时忽略" };
        ignore.Click += (_, _) => RemoveFromUnassigned(app);
        menu.Items.Add(ignore);

        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---------- 分组与常用区 ----------

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Show(this, "给分组起个名字（如：工作 / 游戏 / 常用工具）", "", light: false);
        if (string.IsNullOrWhiteSpace(name)) return;

        _vm.AddGroup(name.Trim());
        RefreshGroupZones();
    }

    private void _applyAppearanceSafe()
    {
        try { _applyAppearance(); } catch { /* 忽略 */ }
    }

    /// <summary>重绘右侧「常用 + 各分组」放置区。</summary>
    private void RefreshGroupZones()
    {
        GroupZonesHost.Children.Clear();

        // 常用（Dock）
        GroupZonesHost.Children.Add(CreateZone(
            "常用（Dock 上显示）", _vm.DockPins.Count,
            () => _vm.DockPins
                .Select(p => (p.Icon, p.Name, FindScanned(p.Model.Target)))
                .ToList(),
            app => AddToDock(app)));

        // 各分组
        foreach (var group in _vm.Groups.ToList())
        {
            var g = group;
            GroupZonesHost.Children.Add(CreateZone(
                g.Name, g.Items.Count,
                () => g.Items
                    .Select(i => (i.Icon, i.Name, FindScanned(i.Model.Target)))
                    .ToList(),
                app => AddToGroup(app, g)));
        }

        if (_vm.Groups.Count == 0)
        {
            GroupZonesHost.Children.Add(new TextBlock
            {
                Text = "还没有分组。\n点右上角「+ 新建分组」，起个名字，\n然后把左边的程序拖进来。",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x82, 0x90)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(4, 10, 0, 0),
            });
        }
    }

    /// <summary>按目标路径找回扫描记录（用于把分组/常用里的图标再拖走）。</summary>
    private ScannedApp? FindScanned(string target)
        => _all.FirstOrDefault(a =>
            string.Equals(a.Item.Target, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>创建一个可放置的分组区（区内图标可再次拖走）。</summary>
    private Border CreateZone(string title, int count,
        Func<List<(ImageSource? Icon, string Name, ScannedApp? App)>> itemsGetter,
        Action<ScannedApp> onDrop,
        Action<ScannedApp>? onItemDragOut = null)
    {
        var header = new TextBlock
        {
            Text = $"{title}  ·  {count}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x72)),
        };

        var content = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (icon, n, app) in itemsGetter())
        {
            var img = new Image
            {
                Source = icon,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                ToolTip = app is null ? n : $"{n}（可再拖到别处）",
                Tag = app,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            img.Margin = new Thickness(0, 0, 6, 4);

            // v1.26：区内图标可继续拖动 —— 拖到其它区或拖回左侧即可改错
            if (app is not null)
            {
                img.Cursor = Cursors.Hand;
                img.MouseLeftButtonDown += (_, e) =>
                {
                    _zoneDragCandidate = app;
                    _dragStart = e.GetPosition(this);
                };
                img.MouseMove += (s, e) =>
                {
                    if (_zoneDragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
                    var pos = e.GetPosition(this);
                    if (Math.Abs(pos.X - _dragStart.X) + Math.Abs(pos.Y - _dragStart.Y)
                        < SystemParameters.MinimumHorizontalDragDistance) return;
                    var dragged = _zoneDragCandidate;
                    _zoneDragCandidate = null;
                    try
                    {
                        var data = new DataObject(DragFormat, dragged);
                        DragDrop.DoDragDrop((DependencyObject)s!, data, DragDropEffects.Copy);
                    }
                    catch { /* 取消 */ }
                };
            }

            content.Children.Add(img);
        }

        if (content.Children.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "把左边的程序拖到这里",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x9C)),
                Margin = new Thickness(2, 2, 0, 2),
            });
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(content);

        var zone = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xF3, 0xF7)),
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack,
            AllowDrop = true,
        };

        zone.DragOver += (_, e) =>
        {
            var ok = e.Data.GetDataPresent(DragFormat);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            zone.Background = new SolidColorBrush(ok
                ? Color.FromArgb(0x33, 0x7C, 0x6F, 0xFF)
                : Color.FromArgb(0xFF, 0xF2, 0xF3, 0xF7));
            e.Handled = true;
        };
        zone.DragLeave += (_, _) =>
            zone.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xF3, 0xF7));
        zone.Drop += (_, e) =>
        {
            zone.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xF3, 0xF7));
            if (e.Data.GetData(DragFormat) is ScannedApp app) onDrop(app);
            e.Handled = true;
        };

        return zone;
    }

    private void AddToGroup(ScannedApp app, GroupVM group)
    {
        if (group.Items.Any(i => string.Equals(i.Model.Target, app.Item.Target, StringComparison.OrdinalIgnoreCase)))
        {
            RemoveEverywhere(app, exceptGroup: group);
            RefreshUnassigned();
            RefreshGroupZones();
            return;
        }

        RemoveEverywhere(app, exceptGroup: group);   // v1.26：先从常用/其它分组移除，避免复制成两份
        group.AddItem(new ItemVM(app.Item, _vm.Icons));
        _unassigned.Remove(app);
        RefreshUnassigned();
        RefreshGroupZones();
    }

    private void AddToDock(ScannedApp app)
    {
        if (_vm.DockPins.Any(p => string.Equals(p.Model.Target, app.Item.Target, StringComparison.OrdinalIgnoreCase)))
        {
            RemoveEverywhere(app, exceptDock: true);
            RefreshUnassigned();
            RefreshGroupZones();
            return;
        }

        RemoveEverywhere(app, exceptDock: true);
        _vm.PinItemDirect(app.Item);
        _unassigned.Remove(app);
        RefreshUnassigned();
        RefreshGroupZones();
    }

    /// <summary>把程序从所有分组与常用中移除（可保留指定目标）。</summary>
    private void RemoveEverywhere(ScannedApp app, GroupVM? exceptGroup = null, bool exceptDock = false)
    {
        foreach (var g in _vm.Groups.ToList())
        {
            if (exceptGroup != null && ReferenceEquals(g, exceptGroup)) continue;
            var hit = g.Items.FirstOrDefault(i =>
                string.Equals(i.Model.Target, app.Item.Target, StringComparison.OrdinalIgnoreCase));
            if (hit != null) g.RemoveItem(hit);
        }

        if (!exceptDock)
        {
            var pin = _vm.DockPins.FirstOrDefault(p =>
                string.Equals(p.Model.Target, app.Item.Target, StringComparison.OrdinalIgnoreCase));
            if (pin != null) _vm.Unpin(pin);
        }
    }

    /// <summary>v1.26：拖回左侧 —— 从分组/常用移除，重新变回"未整理"。</summary>
    private void ReturnToUnassigned(ScannedApp app)
    {
        RemoveEverywhere(app);
        if (!_unassigned.Contains(app)) _unassigned.Add(app);
        RefreshUnassigned();
        RefreshGroupZones();
    }

    private void RemoveFromUnassigned(ScannedApp app)
    {
        _unassigned.Remove(app);
        RefreshUnassigned();
    }

    // ---------- 外观 ----------

    private void BuildAccentPalette()
    {
        foreach (var hex in new[] { "#7C6FFF", "#FF7C6F", "#4FC3F7", "#66BB6A", "#FFD54F", "#E066FF" })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = MakeSwatch(hex, color, 32);
            swatch.Click += (_, _) => SelectAccent(hex);
            AccentPalette.Children.Add(swatch);
        }
    }

    private void BuildTextColorPalette()
    {
        foreach (var hex in new[] { "#000000", "#FFFFFF", "#7C6FFF", "#4FC3F7", "#FFD54F" })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = MakeSwatch(hex, color, 26);
            swatch.Click += (_, _) => TextColorBox.Text = hex;
            TextColorPalette.Children.Add(swatch);
        }
    }

    /// <summary>
    /// v1.26：色板用 Button 而不是 Border ——
    /// Border 不在 IsInteractive 名单里，点击会被窗口级 DragMove 吞掉（表现为"点了没反应"）。
    /// </summary>
    private static Button MakeSwatch(string hex, Color color, double size)
    {
        return new Button
        {
            Width = size,
            Height = size,
            Tag = hex,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0x00, 0x00, 0x00)),
            ToolTip = hex,
            Template = CreateSwatchTemplate(),
        };
    }

    private static ControlTemplate CreateSwatchTemplate()
    {
            #pragma warning disable CS0618 // 用 FrameworkElementFactory 构建模板（零依赖）
            var inner = new FrameworkElementFactory(typeof(Border));
            inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            inner.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(BackgroundProperty));

            var outer = new FrameworkElementFactory(typeof(Border));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            outer.SetValue(Border.PaddingProperty, new Thickness(2));
            outer.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(BorderThicknessProperty));
            outer.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(BorderBrushProperty));
            outer.AppendChild(inner);
            #pragma warning restore CS0618

            var template = new ControlTemplate(typeof(Button)) { VisualTree = outer };
            return template;
    }

    private void SelectAccent(string hex)
    {
        ApplySwatchSelection(AccentPalette, hex);
        _vm.Store.Config.Settings.AccentColor = hex;
        _applyAppearanceSafe();
    }

    /// <summary>v1.28：选择环 —— 浅色卡片上白色描边看不见，改用强调紫 + 与色块留 2px 间隙。</summary>
    private static void ApplySwatchSelection(StackPanel panel, string? hex)
    {
        var selected = new SolidColorBrush(Color.FromRgb(0x7C, 0x6F, 0xFF));
        var normal = new SolidColorBrush(Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
        foreach (var b in panel.Children.OfType<Button>())
        {
            var match = hex != null && string.Equals(b.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
            b.BorderThickness = new Thickness(match ? 2 : 1);
            b.BorderBrush = match ? selected : normal;
        }
    }

    private void InitAppearanceSelection()
    {
        _appearanceReady = false;

        var mode = _vm.Store.Config.Settings.BackgroundMode;
        AcrylicRadio.IsChecked = string.Equals(mode, "acrylic", StringComparison.OrdinalIgnoreCase);
        InvisibleRadio.IsChecked = string.Equals(mode, "invisible", StringComparison.OrdinalIgnoreCase);
        TransparentRadio.IsChecked = AcrylicRadio.IsChecked != true && InvisibleRadio.IsChecked != true;
        SelectAccent(_vm.Store.Config.Settings.AccentColor);
        TextColorBox.Text = _vm.Store.Config.Settings.DockTextColor;
        ApplySwatchSelection(TextColorPalette, _vm.Store.Config.Settings.DockTextColor);

        _appearanceReady = true;
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_appearanceReady) return;
        _vm.Store.Config.Settings.BackgroundMode =
            AcrylicRadio.IsChecked == true ? "acrylic"
            : InvisibleRadio.IsChecked == true ? "invisible"
            : "transparent";
        _applyAppearanceSafe();
    }

    private void TextColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_appearanceReady) return;
        var hex = TextColorBox.Text.Trim();
        if (hex.Length == 0) return;
        try { _ = (Color)ColorConverter.ConvertFromString(hex); }
        catch { return; }
        _vm.Store.Config.Settings.DockTextColor = hex;
        ApplySwatchSelection(TextColorPalette, hex);
        _applyAppearanceSafe();
    }

    // ---------- 完成 ----------

    private void UpdateDoneSummary()
    {
        var groups = _vm.Groups.Count(g => g.Items.Count > 0);
        var items = _vm.Groups.Sum(g => g.Items.Count);
        var pins = _vm.DockPins.Count;

        if (groups == 0 && pins == 0)
        {
            DoneSummaryText.Text =
                "还没有放入任何程序。\n" +
                "没关系，之后可以随时从桌面拖快捷方式到面板，或在 Dock 上右键添加。";
        }
        else
        {
            DoneSummaryText.Text =
                $"已建好 {groups} 个分组，放入 {items} 个程序；常用（Dock）里放了 {pins} 个。\n" +
                "之后随时可以继续调整。";
        }
        DoneHotkeyRun.Text = _vm.Store.Config.Settings.Hotkey;
    }

    // ---------- 工具 ----------

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBoxBase or ComboBox or ScrollBar or CheckBox or RadioButton)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
