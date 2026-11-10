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

public partial class PanelWindow : Window
{
    private readonly MainViewModel _vm;
    private DockBarWindow? _dock;
    private bool _hiding;
    private bool _transparent;
    private bool _oleDragging;
    private int _suppressHide; // v1.7：子对话框/菜单打开时抑制自动隐藏
    private readonly Dictionary<Border, Point> _dragAnchors = new();

    public PanelWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        // v1.1：acrylic=毛玻璃背板；transparent/invisible=分层窗口（ AllowsTransparency）
        _transparent = !string.Equals(vm.Store.Config.Settings.BackgroundMode, "acrylic", StringComparison.OrdinalIgnoreCase);
        if (_transparent) AllowsTransparency = true;
        // v1.1：透明/隐形模式关闭 DWM 玻璃垫层（整窗"雾膜"根源，见 DockBarWindow）
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null)
            chrome.GlassFrameThickness = new Thickness(0);

        // F31 v0.8：点击其他窗口（面板失活）→ 立即收回；光标仍在 Dock 上除外
        Deactivated += (_, _) =>
        {
            // v1.7：打开子对话框/右键菜单/消息框时不收回（否则重命名时面板会消失）
            if (_suppressHide > 0) return;
            try
            {
                if (_dock is { IsVisible: true })
                {
                    Win32.GetCursorPos(out var cur);
                    var r = _dock.GetScreenRect();
                    if (cur.X >= r.Left && cur.X <= r.Right && cur.Y >= r.Top && cur.Y <= r.Bottom)
                        return;
                }
            }
            catch { /* 检测失败按隐藏处理 */ }
            HidePanel();
        };

        // v1.7：全局 Esc / Enter（不依赖搜索框焦点）
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (SearchBox.Text.Length > 0) SearchBox.Clear();
                else HidePanel();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SearchBox.Text.Trim().Length > 0)
            {
                if (SearchResults.Items.Count > 0 && SearchResults.Items[0] is ItemVM first)
                    LaunchAndClose(first);
                e.Handled = true;
            }
        };

        SourceInitialized += (_, _) =>
        {
            var invisible = Theme.NearInvisible;

            // v1.2：分层窗口不碰 DWM 属性（CAPTION/BORDER/CORNER 会留下可见色块"雾膜"）
            if (_transparent)
            {
                if (invisible)
                {
                    RootBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    // 透明模式：柔和阴影把图标从壁纸中衬出来
                    RootBorder.Effect = new DropShadowEffect
                    {
                        BlurRadius = 28,
                        ShadowDepth = 0,
                        Opacity = 0.5,
                    };
                }
                return;
            }

            // 毛玻璃模式：DWM 背板 + 圆角
            var level = Acrylic.Apply(new WindowInteropHelper(this).Handle, useBackdrop: true);
            if (level == AcrylicLevel.Solid)
                RootBorder.Background = (Brush)FindResource("SolidSurfaceBrush");
        };
    }

    public void SetDock(DockBarWindow dock) => _dock = dock;

    public Win32.RECT GetScreenRect()
    {
        Win32.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        return rect;
    }

    /// <summary>F32：隐藏=窗口隐藏（非销毁），呼出 &lt;16ms。</summary>
    public void ShowPanel()
    {
        Reposition();
        ClearAnims();
        Opacity = 0;
        Show();
        // §6.3：对 Hidden 窗口调 Activate() 抛异常 → Show() + 延迟激活 + Topmost 兜底
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try { Activate(); }
            catch { /* 激活失败不影响显示 */ }
            Topmost = true;
            // v1.7：打开即聚焦搜索框，可直接打字
            try
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            }
            catch { /* 聚焦失败不影响使用 */ }
        }));
        PlayOpen();
    }

    public void HidePanel()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        // v1.7：关闭时清空搜索，下次打开是干净状态
        try
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
        }
        catch { /* 忽略 */ }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(26, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            Hide();
            _hiding = false;
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void ClearAnims()
    {
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        BeginAnimation(OpacityProperty, null);
        _hiding = false;
    }

    /// <summary>F8：iOS 式弹出动画（§9.1 参数表）。</summary>
    private void PlayOpen()
    {
        RootScale.CenterX = ActualWidth / 2;
        RootScale.CenterY = ActualHeight / 2;
        RootScale.ScaleX = 0.94;
        RootScale.ScaleY = 0.94;
        RootTranslate.Y = 30;

        // 缩放 0.94→1，260ms BackEase(EaseOut, amp 0.45) 回弹
        var scale = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 },
        };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        // 上浮 Y 30→0，220ms CubicEase(EaseOut)
        var rise = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, rise);

        // 淡入 0→Settings.Opacity，180ms
        var target = _vm.Store.Config.Settings.Opacity;
        var fade = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Opacity = target;
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>F42 v0.6：面板整体缩放（窗口尺寸同步，动画中心动态）。</summary>
    public void ApplyScale()
    {
        var s = Math.Clamp(_vm.Store.Config.Settings.PanelScale, 0.8, 1.4);
        ContentScale.ScaleX = s;
        ContentScale.ScaleY = s;
        Width = Math.Round(760 * s);
        Height = Math.Round(440 * s);
        if (IsVisible) Reposition();
    }

    /// <summary>面板相对 Dock 定位：Dock 在上半屏则面板在下，否则在上；水平以 Dock 居中（支持自由位置）。</summary>
    private void Reposition()
    {
        // v1.16：用 Dock 所在显示器的工作区（多屏支持）
        var dock = _dock;
        if (dock is null || !dock.IsLoaded)
        {
            var cursorArea = ScreenHelper.GetWorkAreaAtCursor();
            Left = cursorArea.Left + (cursorArea.Width - Width) / 2;
            Top = cursorArea.Bottom - Height - 72;
            return;
        }

        var wa = ScreenHelper.GetWorkAreaForWindow(dock);

        // v1.20：面板左边缘对齐"当前选中分组按钮"的 x，而不是整条 Dock 居中。
        // 这样悬停「网安」（Dock 最左侧）时，面板不会跑到很右边，用户的手不用长距离移动。
        var anchorX = dock.GetGroupButtonScreenX(_vm.SelectedGroup);
        double left;
        if (anchorX.HasValue)
            left = anchorX.Value;          // 分组按钮左边缘对齐面板左边缘
        else
            left = dock.Left + (dock.ActualWidth - Width) / 2;  // 回退：居中

        double top;
        if (dock.Top < wa.Top + wa.Height / 2)
            top = dock.Top + dock.ActualHeight + 8;  // Dock 在上半屏 → 面板放下方
        else
            top = dock.Top - Height - 8;             // 默认：Dock 上方

        Left = Math.Max(wa.Left, Math.Min(left, wa.Right - Width));
        Top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - Height));
    }

    // ---------- v1.13：分组拖拽排序 ----------

    private void GroupList_Down(object sender, MouseButtonEventArgs e)
        => GroupDragHelper.OnDown(GroupList, e);

    private void GroupList_Move(object sender, MouseEventArgs e)
        => GroupDragHelper.TryStartDrag(GroupList, e);

    private void GroupList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("QuickPanel.Group")
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupList_Drop(object sender, DragEventArgs e)
    {
        GroupDragHelper.OnDrop(GroupList, e, (g, idx) => _vm.MoveGroup(g, idx));
        e.Handled = true;
    }

    // ---------- F60 v0.6：搜索框 ----------

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateSearch();
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SearchBox.Text.Length > 0)
            {
                SearchBox.Clear();
            }
            else
            {
                HidePanel();
            }
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;

        if (SearchResults.Items.Count > 0 && SearchResults.Items[0] is ItemVM first)
        {
            LaunchAndClose(first);
        }
        e.Handled = true;
    }

    private void UpdateSearch()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            SearchHost.Visibility = Visibility.Collapsed;
            ItemsHost.Visibility = Visibility.Visible;
            SearchResults.ItemsSource = null;
            return;
        }

        ItemsHost.Visibility = Visibility.Collapsed;
        SearchHost.Visibility = Visibility.Visible;
        SearchResults.ItemsSource = _vm.SearchAll(query);
    }

    private void LaunchAndClose(ItemVM item)
    {
        _vm.LaunchItem(item);
        SearchBox.Clear();
        UpdateSearch();
        HidePanel();
    }

    // ---------- 图标动效（F12/F55）----------

    private static void AnimateItemScale(Border border, double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        // DataTemplate 内匿名 Freezable 会被密封冻结，冻结时换新的可动画实例
        ScaleTransform scale;
        if (border.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
        {
            scale = existing;
        }
        else
        {
            scale = new ScaleTransform(1, 1);
            border.RenderTransform = scale; // 缩放中心由 RenderTransformOrigin 提供
        }
        var anim = new DoubleAnimation(to, duration) { EasingFunction = easing };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void Item_Enter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            AnimateItemScale(border, 1.12, TimeSpan.FromMilliseconds(120),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
    }

    private void Item_Leave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            AnimateItemScale(border, 1.0, TimeSpan.FromMilliseconds(140),
                new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    // ---------- 拖出（F3 v0.5：FileDrop 落到桌面生成副本）----------

    private void Item_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && e.LeftButton == MouseButtonState.Pressed)
            _dragAnchors[border] = e.GetPosition(border);
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || !_dragAnchors.TryGetValue(border, out var anchor)) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragAnchors.Remove(border);
            return;
        }

        var pos = e.GetPosition(border);
        if (Math.Abs(pos.X - anchor.X) + Math.Abs(pos.Y - anchor.Y)
            < SystemParameters.MinimumHorizontalDragDistance) return;

        _dragAnchors.Remove(border);
        if (border.Tag is not ItemVM item) return;
        StartItemDrag(border, item); // v1.17：携带 QuickPanel.Item 供 Dock 钉选
    }

    /// <summary>
    /// v1.17：从面板拖出条目。
    /// 同时携带 QuickPanel.Item（供 Dock 接收 → 钉选）与 FileDrop（供桌面接受 → 生成副本）。
    /// URL 类无实体文件时只带 QuickPanel.Item，因此仍可拖到 Dock 钉选。
    /// </summary>
    private void StartItemDrag(FrameworkElement source, ItemVM item)
    {
        _oleDragging = true;
        var originalOpacity = source.Opacity;
        DragGhost? ghost = null;
        try
        {
            var data = new DataObject();
            data.SetData("QuickPanel.Item", item);
            var target = item.Model.Target;
            if (File.Exists(target) || Directory.Exists(target))
                data.SetData(DataFormats.FileDrop, new[] { target });

            // v1.19：拖拽幽灵 + 源图标淡出
            try
            {
                ghost = new DragGhost(item.Icon, item.Name, 52);
                source.Opacity = 0.35;
                if (source is Border b)
                    AnimateItemScale(b, 1.0, TimeSpan.FromMilliseconds(100));
            }
            catch { /* ghost 失败不影响拖拽 */ }

            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            ghost?.Dispose();
            source.Opacity = originalOpacity;
            _oleDragging = false;
        }
    }

    private void StartFileDrag(FrameworkElement source, string target)
    {
        // 兼容旧调用：仅文件/文件夹
        if (!File.Exists(target) && !Directory.Exists(target)) return;
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

    // ---------- 点击 / 菜单（F6/F7/F12 v0.6）----------

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (_oleDragging) return;
        if ((sender as FrameworkElement)?.Tag is not ItemVM item) return;

        // F12：点击弹一下 scale 1→0.9→1
        if (sender is Border border)
        {
            var down = new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(80));
            down.Completed += (_, _) => AnimateItemScale(border, 1.0, TimeSpan.FromMilliseconds(130),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
            AnimateItemScale(border, 0.9, TimeSpan.FromMilliseconds(80));
        }

        LaunchAndClose(item);
    }

    private void Item_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if ((sender as FrameworkElement)?.Tag is ItemVM item)
            _vm.LaunchItem(item, asAdmin: true); // v0.6：中键=管理员运行
    }

    private void Item_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ItemVM item) return;

        // F7 v0.6：增强菜单
        var menu = new ContextMenu { PlacementTarget = fe, Placement = PlacementMode.Bottom };

        var open = new MenuItem { Header = "启动", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => LaunchAndClose(item);
        menu.Items.Add(open);

        var admin = new MenuItem { Header = "以管理员运行" };
        admin.Click += (_, _) => _vm.LaunchItem(item, asAdmin: true);
        menu.Items.Add(admin);

        if (File.Exists(item.Model.Target) || Directory.Exists(item.Model.Target))
        {
            var locate = new MenuItem { Header = "打开所在位置" };
            locate.Click += (_, _) => RevealInExplorer(item.Model.Target);
            menu.Items.Add(locate);
        }

        var pin = new MenuItem { Header = _vm.IsPinned(item) ? "从 Dock 取消固定" : "固定到 Dock" };
        pin.Click += (_, _) => _vm.TogglePin(item);
        menu.Items.Add(pin);

        if (item.OwnerGroup != null && _vm.Groups.Count > 1)
        {
            var move = new MenuItem { Header = "移动到分组" };
            foreach (var group in _vm.Groups.Where(g => !ReferenceEquals(g, item.OwnerGroup)))
            {
                var target = group;
                var mi = new MenuItem { Header = group.Name };
                mi.Click += (_, _) => _vm.MoveItem(item, target);
                move.Items.Add(mi);
            }
            menu.Items.Add(move);
        }

        // v1.8：完整编辑（名称/参数/工作目录/管理员）
        var edit = new MenuItem { Header = "编辑…（名称/参数/工作目录）" };
        edit.Click += (_, _) =>
        {
            var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
            _suppressHide++;
            try
            {
                var r = EditItemDialog.Show(this, item.Name, item.Model.Target,
                    item.Model.Args, item.Model.WorkDir, item.Model.RunAsAdmin, light);
                if (r != null)
                {
                    item.Name = r.Name;
                    item.Model.Args = r.Args;
                    item.Model.WorkDir = r.WorkDir;
                    item.Model.RunAsAdmin = r.RunAsAdmin;
                    _vm.SaveConfig();
                }
            }
            finally { _suppressHide--; }
        };
        menu.Items.Add(edit);

        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) =>
        {
            var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
            string? name;
            _suppressHide++; // v1.7：对话框打开期间不收回面板
            try
            {
                name = InputDialog.Show(this, $"重命名「{item.Name}」", item.Name, light);
            }
            finally
            {
                _suppressHide--;
            }
            if (!string.IsNullOrWhiteSpace(name) && name != item.Name)
                item.Name = name;
            _vm.SaveConfig();
        };
        menu.Items.Add(rename);

        var remove = new MenuItem
        {
            // v1.7：按真实归属显示/执行（搜索结果可能来自其他分组）
            Header = item.OwnerGroup != null && !ReferenceEquals(item.OwnerGroup, _vm.SelectedGroup)
                ? $"从「{item.OwnerGroup.Name}」移除"
                : "从分组移除",
        };
        remove.Click += (_, _) => _vm.RemoveFromOwnGroup(item);
        menu.Items.Add(remove);

        menu.IsOpen = true;
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

    // ---------- 拖入（F3）----------

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        RootBorder.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
        => RootBorder.BorderBrush = (Brush)FindResource("HairlineBrush");

    private void Root_Drop(object sender, DragEventArgs e)
    {
        Root_DragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            _vm.AddToCurrentGroup(files);
    }

    /// <summary>v1.7：面板空白处右键 → 添加条目 / 新建分组。</summary>
    private void Root_RightClick(object sender, MouseButtonEventArgs e)
    {
        // 只在空白处触发（图标有自己的菜单）
        if (e.OriginalSource is FrameworkElement fe && fe.Tag is ItemVM) return;

        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };

        var add = new MenuItem { Header = "添加条目…", FontWeight = FontWeights.SemiBold };
        add.Click += (_, _) => ShowAddItemDialog();
        menu.Items.Add(add);

        var newGroup = new MenuItem { Header = "新建分组…" };
        newGroup.Click += (_, _) =>
        {
            var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
            _suppressHide++;
            try
            {
                var name = InputDialog.Show(this, "新分组名称（如：网安 / 游戏 / 剪辑）", "", light);
                if (!string.IsNullOrWhiteSpace(name)) _vm.AddGroup(name);
            }
            finally { _suppressHide--; }
        };
        menu.Items.Add(newGroup);

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>v1.7：添加条目对话框。</summary>
    private void ShowAddItemDialog()
    {
        var light = string.Equals(_vm.Store.Config.Settings.SettingsTheme, "light", StringComparison.OrdinalIgnoreCase);
        _suppressHide++;
        try
        {
            var result = AddItemDialog.Show(this, light);
            if (result is { } r)
            {
                if (!_vm.AddItem(r.Name, r.Target, r.Kind))
                    MessageBox.Show("该目标已存在于当前分组。", "QuickPanel",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally { _suppressHide--; }
    }
}
