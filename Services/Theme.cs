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

        var invisibleMode = string.Equals(config.Settings.BackgroundMode, "invisible", StringComparison.OrdinalIgnoreCase);

        // v0.9：alpha=0 的底色会被系统判定为整窗点击穿透，钳制到 1（肉眼不可见）
        if (surface.A == 0)
            surface = Color.FromArgb(1, surface.R, surface.G, surface.B);

        // v1.1：「完全隐形」模式一键无底板（不再依赖用户手算 AA）；全透明 + AA≤20 同效
        NearInvisible = invisibleMode
            || (string.Equals(config.Settings.BackgroundMode, "transparent", StringComparison.OrdinalIgnoreCase)
                && surface.A <= 0x20);

        if (invisibleMode)
            surface = Color.FromArgb(1, 0, 0, 0); // 隐形薄膜：可点击但不可见

        res["SurfaceOverlayBrush"] = new SolidColorBrush(surface);
        res["AccentBrush"] = new SolidColorBrush(accent);
        res["SelectedBrush"] = new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B));
    }
}
