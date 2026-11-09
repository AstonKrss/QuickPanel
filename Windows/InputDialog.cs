using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPanel.Services;

namespace QuickPanel.Windows;

/// <summary>轻量输入对话框（重命名/新建分组）。v1.0：跟随设置主题深/浅色。</summary>
public static class InputDialog
{
    public static string? Show(Window owner, string title, string value, bool light = false)
    {
        var textBrush = Light(light, "#FF111318", "#FFFFFFFF"); // v1.1：极限对比（浅色近黑/深色纯白）
        var bgBrush = Light(light, "#FFF7F7FA", "#FF17171C");
        var fieldBrush = Light(light, "#FFFFFFFF", "#33FFFFFF");
        var fieldBorder = Light(light, "#22000000", "#66FFFFFF");
        var btnBrush = Light(light, "#FFF0F1F6", "#3DFFFFFF");
        var btnBorder = Light(light, "#2E000000", "#59FFFFFF");

        var label = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = textBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        var box = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 12, 0, 0),
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Background = fieldBrush,
            Foreground = textBrush,
            BorderBrush = fieldBorder,
            CaretBrush = textBrush,
        };
        var ok = new Button
        {
            Content = "确定",
            Width = 84,
            Height = 30,
            IsDefault = true,
            Margin = new Thickness(0, 14, 8, 0),
            Background = btnBrush,
            Foreground = textBrush,
            BorderBrush = btnBorder,
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 84,
            Height = 30,
            IsCancel = true,
            Margin = new Thickness(0, 14, 0, 0),
            Background = btnBrush,
            Foreground = textBrush,
            BorderBrush = btnBorder,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(label);
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "QuickPanel",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = bgBrush,
            Owner = owner,
            Content = panel,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
        };
        ok.Click += (_, _) => window.DialogResult = true;
        box.Focus();

        // v1.0：标题栏/圆角跟随主题
        window.SourceInitialized += (_, _) =>
            Acrylic.ApplyDialogTheme(new WindowInteropHelper(window).Handle, dark: !light);

        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private static SolidColorBrush Light(bool light, string lightHex, string darkHex)
        => new((Color)ColorConverter.ConvertFromString(light ? lightHex : darkHex));
}
