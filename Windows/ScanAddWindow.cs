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
/// v1.23：扫描并添加程序（设置里的「重新扫描」入口）。
/// 勾选的程序会加入指定分组。
/// </summary>
public class ScanAddWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly StackPanel _host = new();
    private readonly List<CheckBox> _boxes = new();
    private List<ScannedApp> _scanned = new();
    private readonly ComboBox _groupPicker = new();

    public ScanAddWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "扫描添加程序";
        Width = 640;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");

        SourceInitialized += (_, _) =>
        {
            try { Acrylic.ApplyDialogTheme(new WindowInteropHelper(this).Handle, dark: true); }
            catch { /* 忽略 */ }
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractive(e.OriginalSource as DependencyObject)) return;
            try { DragMove(); } catch { }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        BuildUi();
        RunScan();
    }

    private void BuildUi()
    {
        var text = new SolidColorBrush(Color.FromRgb(0xDC, 0xDD, 0xE2));
        var sub = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAE));
        var accent = new SolidColorBrush(Color.FromRgb(0x7C, 0x6F, 0xFF));
        var field = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

        // ── 标题区 ──
        var head = new StackPanel();
        head.Children.Add(new TextBlock
        {
            Text = "扫描添加程序",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = text,
        });
        head.Children.Add(new TextBlock
        {
            Text = "勾选要添加的程序，它们会加入下面选择的分组",
            FontSize = 12,
            Foreground = sub,
            Margin = new Thickness(0, 5, 0, 0),
        });

        // ── 分组选择 + 批量按钮 ──
        _groupPicker.ItemsSource = _vm.Groups.Select(g => g.Name).ToList();
        if (_groupPicker.Items.Count > 0) _groupPicker.SelectedIndex = 0;
        _groupPicker.Width = 150;
        _groupPicker.Height = 30;
        _groupPicker.Margin = new Thickness(8, 0, 0, 0);
        _groupPicker.Background = field;
        _groupPicker.Foreground = text;

        var pickerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        pickerRow.Children.Add(new TextBlock
        {
            Text = "添加到分组：",
            FontSize = 12.5,
            Foreground = sub,
            VerticalAlignment = VerticalAlignment.Center,
        });
        pickerRow.Children.Add(_groupPicker);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        actions.Children.Add(MakeButton("全选", () => { foreach (var b in _boxes) b.IsChecked = true; }, text));
        actions.Children.Add(MakeButton("全不选", () => { foreach (var b in _boxes) b.IsChecked = false; }, text));
        actions.Children.Add(MakeButton("重新扫描", RunScan, text));

        var toolbar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        toolbar.Children.Add(pickerRow);
        toolbar.Children.Add(actions);

        // ── 列表 ──
        var listHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _host,
            },
        };

        // ── 底部按钮 ──
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var btnCancel = MakeButton("取消", Close, text);
        btnCancel.Padding = new Thickness(20, 8, 20, 8);
        btnCancel.Margin = new Thickness(0, 0, 10, 0);
        footer.Children.Add(btnCancel);

        var btnAdd = new Button
        {
            Content = "添加所选",
            Padding = new Thickness(26, 8, 26, 8),
            Background = accent,
            Foreground = Brushes.White,
            BorderBrush = accent,
            Cursor = Cursors.Hand,
            FontSize = 13,
        };
        btnAdd.Click += (_, _) => AddSelected();
        footer.Children.Add(btnAdd);

        // ── 组装 ──
        var root = new Grid { Margin = new Thickness(28, 22, 28, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 标题
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 工具条
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 列表
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 底部

        Grid.SetRow(head, 0); root.Children.Add(head);
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        Grid.SetRow(listHost, 2); root.Children.Add(listHost);
        Grid.SetRow(footer, 3); root.Children.Add(footer);

        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = root,
        };
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBoxBase or ComboBox or ScrollBar) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static Button MakeButton(string content, Action onClick, Brush fg)    {
        var b = new Button
        {
            Content = content,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(6, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = fg,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void RunScan()
    {
        _host.Children.Clear();
        _boxes.Clear();
        try { _scanned = AppScanner.Scan(); }
        catch { _scanned = new List<ScannedApp>(); }

        if (_scanned.Count == 0)
        {
            _host.Children.Add(new TextBlock
            {
                Text = "没有找到快捷方式",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAE)),
                FontSize = 12,
            });
            return;
        }

        foreach (var groupId in new[] { "dev", "office", "media", "game", "social", "tools" })
        {
            var apps = _scanned.Where(a => a.GroupId == groupId).ToList();
            if (apps.Count == 0) continue;

            _host.Children.Add(new TextBlock
            {
                Text = $"{AppScanner.GroupDisplayName(groupId)} · {apps.Count}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x6F, 0xFF)),
                Margin = new Thickness(0, _host.Children.Count == 0 ? 0 : 12, 0, 5),
            });

            var wrap = new WrapPanel();
            foreach (var app in apps)
            {
                var cb = new CheckBox
                {
                    Content = app.Item.Name,
                    IsChecked = false,
                    Tag = app,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDD, 0xE2)),
                    Margin = new Thickness(0, 3, 14, 3),
                    Width = 180,
                    Cursor = Cursors.Hand,
                };
                _boxes.Add(cb);
                wrap.Children.Add(cb);
            }
            _host.Children.Add(wrap);
        }
    }

    private void AddSelected()
    {
        var picked = _boxes.Where(b => b.IsChecked == true)
            .Select(b => b.Tag as ScannedApp)
            .Where(a => a is not null)
            .Cast<ScannedApp>()
            .ToList();

        if (picked.Count == 0)
        {
            MessageBox.Show("请先勾选要添加的程序。", "QuickPanel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = _vm.SelectedGroup;
        var idx = _groupPicker.SelectedIndex;
        if (idx >= 0 && idx < _vm.Groups.Count) target = _vm.Groups[idx];
        if (target is null) return;

        var added = 0;
        foreach (var app in picked)
        {
            if (target.Items.Any(i =>
                    string.Equals(i.Model.Target, app.Item.Target, StringComparison.OrdinalIgnoreCase)))
                continue;
            target.AddItem(new ItemVM(app.Item, _vm.Icons));
            added++;
        }
        _vm.SaveConfig();

        MessageBox.Show($"已添加 {added} 个程序到「{target.Name}」。", "QuickPanel",
            MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }
}
