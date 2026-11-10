namespace QuickPanel.Models;

/// <summary>全局设置（PRD §8 数据模型；v0.5 增加外观/Dock 自定义字段）。</summary>
public class AppSettings
{
    public string Hotkey { get; set; } = "Alt+Q";
    public string PanelEdge { get; set; } = "bottom";
    public int HotZoneMs { get; set; } = 200;
    /// <summary>F35：全屏时自动隐藏 Dock 与面板。</summary>
    public bool DockAutoHide { get; set; } = true;
    public string Theme { get; set; } = "dark";
    public double Opacity { get; set; } = 0.92;

    /// <summary>面板整体缩放 0.8–1.4（v0.6）。</summary>
    public double PanelScale { get; set; } = 1.0;

    /// <summary>v0.9：鼠标离开面板+Dock 后自动收回延迟（ms）。</summary>
    public int PanelHideMs { get; set; } = 300;

    /// <summary>v0.8：Dock 图标底板。true=带底板（悬停高亮），false=纯图标无边框。</summary>
    public bool DockIconPlate { get; set; } = true;

    /// <summary>v1.4：Dock 文字颜色 #RRGGBB（纯图标模式下用于分组名/手柄/齿轮）。</summary>
    public string DockTextColor { get; set; } = "#000000";

    /// <summary>v1.4：Dock 仅在桌面显示——打开其他软件时自动隐藏。</summary>
    public bool DockDesktopOnly { get; set; } = true;

    /// <summary>v1.7：Dock 是否已播种过。用户清空钉选后不再自动回填。</summary>
    public bool DockSeeded { get; set; }

    // v1.9 组件
    /// <summary>Dock 左侧显示时钟 + 日期。</summary>
    public bool ShowClock { get; set; }

    /// <summary>Dock 显示系统监控（CPU/内存/网速）。</summary>
    public bool ShowSystemMonitor { get; set; }

    /// <summary>是否在时钟上显示秒。</summary>
    public bool ClockShowSeconds { get; set; }

    /// <summary>v1.10：组件（时钟/监控）字体缩放 0.7–1.6，独立于 Dock 图标缩放。</summary>
    public double WidgetScale { get; set; } = 1.0;

    // v1.12 Dock 样式细调
    /// <summary>图钉图标尺寸（px，24–64）。</summary>
    public double PinSize { get; set; } = 44;

    /// <summary>图钉间距（px，0–16）。</summary>
    public double PinSpacing { get; set; } = 6;

    /// <summary>图钉圆角（px，0–22）。</summary>
    public double PinCornerRadius { get; set; } = 12;

    /// <summary>悬停放大倍数（1.0–1.8）。</summary>
    public double HoverScale { get; set; } = 1.32;

    /// <summary>悬停上浮距离（px，0–20）。</summary>
    public double HoverLift { get; set; } = 8;

    /// <summary>v0.8：设置窗口主题 dark / light。</summary>
    public string SettingsTheme { get; set; } = "dark";

    // v0.5 外观自定义
    /// <summary>背景模式：acrylic（毛玻璃）/ transparent（全透明+自定义底色）/ invisible（完全隐形无底板，v1.1）。</summary>
    public string BackgroundMode { get; set; } = "transparent";

    /// <summary>面板/Dock 底色 #AARRGGBB（AA=透明度，00 为全透）。</summary>
    public string SurfaceColor { get; set; } = "#3C151518";

    /// <summary>强调色 #RRGGBB（选中态自动派生同色低透明）。</summary>
    public string AccentColor { get; set; } = "#7C6FFF";

    // v0.5 Dock 自定义
    /// <summary>位置：bottomCenter（默认）/ free（⠿ 手柄拖动）。</summary>
    public string DockPositionMode { get; set; } = "bottomCenter";

    public double DockX { get; set; }
    public double DockY { get; set; }

    /// <summary>v0.7：Dock 置顶显示。默认 false——只在桌面层级，其他软件窗口自然覆盖。</summary>
    public bool DockTopmost { get; set; }

    /// <summary>图标缩放 0.6–1.6。</summary>
    public double IconScale { get; set; } = 1.0;
}

/// <summary>config.json 根对象。</summary>
public class AppConfig
{
    public AppSettings Settings { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<LaunchItem> DockPins { get; set; } = new();
}
