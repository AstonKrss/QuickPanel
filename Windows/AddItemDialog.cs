using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using QuickPanel.Services;

namespace QuickPanel.Windows;

/// <summary>v1.7：添加条目对话框 —— 文件 / 文件夹 / 网址。</summary>
public static class AddItemDialog
{
    /// <summary>返回 (名称, 目标, 类型)；取消返回 null。</summary>
    public static (string Name, string Target, Models.ItemKind Kind)? Show(Window owner, bool light)
    {
        var text = Light(light, "#FF111318", "#FFFFFFFF");
        var bg = Light(light, "#FFF7F7FA", "#FF17171C");
        var field = Light(light, "#FFFFFFFF", "#33FFFFFF");
        var fieldBorder = Light(light, "#22000000", "#66FFFFFF");
        var btn = Light(light, "#FFF0F1F6", "#3DFFFFFF");
        var btnBorder = Light(light, "#2E000000", "#59FFFFFF");
        var accent = (Brush)Application.Current.Resources["AccentBrush"];

        var nameBox = new TextBox
        {
            Height = 34,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = field,
            Foreground = text,
            BorderBrush = fieldBorder,
            CaretBrush = text,
        };
        var targetBox = new TextBox
        {
            Height = 34,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = field,
            Foreground = text,
            BorderBrush = fieldBorder,
            CaretBrush = text,
        };

        var browseFile = new Button { Content = "选文件…", Padding = new Thickness(10, 4, 10, 4) };
        var browseDir = new Button { Content = "选文件夹…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        foreach (var b in new[] { browseFile, browseDir })
        {
            b.Background = btn;
            b.Foreground = text;
            b.BorderBrush = btnBorder;
        }

        browseFile.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "可执行/快捷方式|*.exe;*.lnk;*.url|所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            targetBox.Text = dlg.FileName;
            if (nameBox.Text.Trim().Length == 0)
                nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        };
        browseDir.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() != true) return;
            targetBox.Text = dlg.FolderName;
            if (nameBox.Text.Trim().Length == 0)
                nameBox.Text = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\'));
        };

        var browseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        browseRow.Children.Add(browseFile);
        browseRow.Children.Add(browseDir);

        var ok = new Button
        {
            Content = "添加",
            Width = 84,
            Height = 32,
            IsDefault = true,
            Margin = new Thickness(0, 16, 8, 0),
            Background = accent,
            Foreground = Brushes.White,
            BorderBrush = accent,
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 84,
            Height = 32,
            IsCancel = true,
            Margin = new Thickness(0, 16, 0, 0),
            Background = btn,
            Foreground = text,
            BorderBrush = btnBorder,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "添加条目",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = text,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(new TextBlock { Text = "名称", Foreground = text, FontSize = 12 });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "目标（文件 / 文件夹 / 网址）", Foreground = text, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(targetBox);
        panel.Children.Add(browseRow);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "QuickPanel — 添加条目",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = bg,
            Owner = owner,
            Content = panel,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
        };
        window.SourceInitialized += (_, _) =>
            Acrylic.ApplyDialogTheme(new WindowInteropHelper(window).Handle, dark: !light);
        window.Loaded += (_, _) => nameBox.Focus();

        ok.Click += (_, _) =>
        {
            if (targetBox.Text.Trim().Length == 0) return;
            window.DialogResult = true;
        };

        if (window.ShowDialog() != true) return null;

        var target = targetBox.Text.Trim();
        var name = nameBox.Text.Trim();
        if (name.Length == 0)
            name = System.IO.Path.GetFileNameWithoutExtension(target);

        var kind = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Models.ItemKind.Url
            : System.IO.Directory.Exists(target) ? Models.ItemKind.Folder
            : Models.ItemKind.App;

        return (name, target, kind);
    }

    private static SolidColorBrush Light(bool light, string lightHex, string darkHex)
        => new((Color)ColorConverter.ConvertFromString(light ? lightHex : darkHex));
}
