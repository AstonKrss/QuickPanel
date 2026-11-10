using System.Windows;
using System.Windows.Media;
using QuickPanel.Models;

namespace QuickPanel.Services;

    /// <summary>视觉令牌运行时应用（PRD §9.2；v0.5 支持底色/强调色自定义）。</summary>
    public static class Theme
    {
        /// <summary>v1.0：全透明模式且底色 AA≤20 → 面板/Dock 完全隐形（无阴影无描边，仅内容）。</summary>
        public static bool NearInvisible { get; private set; }

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>把 config 中的颜色写入 Application.Resources；XAML 侧用 DynamicResource 引用即实时更新。</summary>
    public static void Apply(AppConfig config)
    {
        var res = Application.Current.Resources;

        var surface = ParseColor(config.Settings.SurfaceColor, Color.FromArgb(0xC0, 0x15, 0x15, 0x18));
        var accent = ParseColor(config.Settings.AccentColor, Color.FromRgb(0x7C, 0x6F, 0xFF));

        var mode = config.Settings.BackgroundMode;
        var invisibleMode = string.Equals(mode, "invisible", StringComparison.OrdinalIgnoreCase);
        var acrylicMode = string.Equals(mode, "acrylic", StringComparison.OrdinalIgnoreCase);

        // v0.9：alpha=0 的底色会被系统判定为整窗点击穿透，钳制到 1（肉眼不可见）
        if (surface.A == 0)
            surface = Color.FromArgb(1, surface.R, surface.G, surface.B);

        // v1.1：完全隐形模式一键无底板；全透明 + AA≤20 同效
        NearInvisible = invisibleMode
            || (string.Equals(mode, "transparent", StringComparison.OrdinalIgnoreCase)
                && surface.A <= 0x20);

        if (invisibleMode)
            surface = Color.FromArgb(1, 0, 0, 0); // 隐形薄膜：可点击但不可见

        // v1.27："毛玻璃"改为分层窗口内的半透明深色板（见下方说明），固定视觉
        if (acrylicMode)
            surface = Color.FromArgb(0xD8, 0x1B, 0x1B, 0x20);

        res["SurfaceOverlayBrush"] = new SolidColorBrush(surface);
        res["AccentBrush"] = new SolidColorBrush(accent);
        res["SelectedBrush"] = new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B));

        // v1.28：底板很淡（半透明/完全透明）时，面板文字跟随用户设置的 Dock 文字色。
        // 浅色壁纸 + 浅色文字 = 分组名看不见；用户选的颜色本来就是给"浮在背景上"准备的。
        var dockText = ParseColor(config.Settings.DockTextColor, Color.FromRgb(0xFF, 0xFF, 0xFF));
        var dockTextBase = surface.A < 0x60 ? dockText : TextOn(surface);
        res["TextPrimaryBrush"] = new SolidColorBrush(dockTextBase);
        res["TextSecondaryBrush"] = new SolidColorBrush(WithAlpha(dockTextBase, 0xB4));
        res["CardSubtleBrush"] = new SolidColorBrush(WithAlpha(dockTextBase, 0x1F));
        res["HairlineBrush"] = new SolidColorBrush(WithAlpha(dockTextBase, 0x33));

        // v1.29：面板有独立底板（颜色 + 不透明度），不再跟着 Dock 的"几乎全透"走。
        var panelRgb = ParseColor(config.Settings.PanelSurfaceColor, Color.FromRgb(0x15, 0x15, 0x18));
        var panelA = (byte)Math.Clamp(Math.Round(config.Settings.PanelOpacity * 255), 32, 255);
        var panelSurface = Color.FromArgb(panelA, panelRgb.R, panelRgb.G, panelRgb.B);
        res["PanelSurfaceBrush"] = new SolidColorBrush(panelSurface);

        // 面板文字：底板够实按底色明暗取；底板很淡则沿用用户挑的"浮在壁纸上"的颜色
        var panelText = panelA < 0x60 ? dockText : TextOn(panelRgb);
        res["PanelTextBrush"] = new SolidColorBrush(panelText);
        res["PanelTextSecondaryBrush"] = new SolidColorBrush(WithAlpha(panelText, 0xB4));
        res["PanelCardBrush"] = new SolidColorBrush(WithAlpha(panelText, 0x2A));
        res["PanelCardSubtleBrush"] = new SolidColorBrush(WithAlpha(panelText, 0x18));
        res["PanelHairlineBrush"] = new SolidColorBrush(WithAlpha(panelText, 0x2E));
    }

    /// <summary>按底色亮度选黑/白文字（阈值取 sRGB 感知亮度）。</summary>
    private static Color TextOn(Color background)
    {
        var luma = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        return luma > 150
            ? Color.FromRgb(0x2A, 0x2F, 0x3A)
            : Color.FromRgb(0xDC, 0xDD, 0xE2);
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
