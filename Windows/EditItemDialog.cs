using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPanel.Services;

namespace QuickPanel.Windows;

/// <summary>v1.8：编辑条目属性 —— 名称 / 启动参数 / 工作目录 / 管理员运行。</summary>
public static class EditItemDialog
{
    public record Result(string Name, string Args, string WorkDir, bool RunAsAdmin);

    public static Result? Show(Window owner, string currentName, string target,
        string currentArgs, string currentWorkDir, bool currentAdmin, bool light)
    {
        var text = Light(light, "#FF111318", "#FFFFFFFF");
        var sub = Light(light, "#FF6A7080", "#FF9AA0AE");
        var bg = Light(light, "#FFF7F7FA", "#FF17171C");
        var field = Light(light, "#FFFFFFFF", "#33FFFFFF");
        var fieldBorder = Light(light, "#22000000", "#66FFFFFF");
        var btn = Light(light, "#FFF0F1F6", "#3DFFFFFF");
        var btnBorder = Light(light, "#2E000000", "#59FFFFFF");
        var accent = (Brush)Application.Current.Resources["AccentBrush"];

        TextBox MakeBox(string value, bool readOnly = false) => new()
        {
            Text = value,
            Height = 32,
            Margin = new Thickness(0, 5, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = readOnly ? sub : field,
            Foreground = text,
            BorderBrush = fieldBorder,
            CaretBrush = text,
            IsReadOnly = readOnly,
            FontSize = 12.5,
        };

        var nameBox = MakeBox(currentName);
        var targetBox = MakeBox(target, readOnly: true);
        var argsBox = MakeBox(currentArgs);
        var workDirBox = MakeBox(currentWorkDir);
        var adminCheck = new CheckBox
        {
            Content = "以管理员身份运行（每次启动弹 UAC）",
            IsChecked = currentAdmin,
            Foreground = text,
            Margin = new Thickness(0, 12, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        var ok = new Button
        {
            Content = "保存",
            Width = 84,
            Height = 32,
            IsDefault = true,
            Margin = new Thickness(0, 18, 8, 0),
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
            Margin = new Thickness(0, 18, 0, 0),
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
        void Label(string t, double top = 14) => panel.Children.Add(new TextBlock
        {
            Text = t,
            Foreground = sub,
            FontSize = 11.5,
            Margin = new Thickness(0, top, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "编辑条目",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = text,
        });
        Label("名称", 14);
        panel.Children.Add(nameBox);
        Label("目标（只读）");
        panel.Children.Add(targetBox);
        Label("启动参数（可留空）");
        panel.Children.Add(argsBox);
        Label("工作目录（可留空，默认取目标所在目录）");
        panel.Children.Add(workDirBox);
        panel.Children.Add(adminCheck);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "QuickPanel — 编辑条目",
            Width = 460,
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
        window.Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };

        ok.Click += (_, _) => window.DialogResult = true;

        if (window.ShowDialog() != true) return null;

        var name = nameBox.Text.Trim();
        if (name.Length == 0) name = currentName;
        return new Result(name, argsBox.Text.Trim(), workDirBox.Text.Trim(), adminCheck.IsChecked == true);
    }

    private static SolidColorBrush Light(bool light, string lightHex, string darkHex)
        => new((Color)ColorConverter.ConvertFromString(light ? lightHex : darkHex));
}
