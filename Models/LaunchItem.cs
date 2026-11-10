using System.Text.Json.Serialization;

namespace QuickPanel.Models;

/// <summary>条目类型（JSON 序列化为字符串，见 PRD §8）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemKind
{
    App,
    Document,
    Url,
    Folder,
}

/// <summary>单个启动条目（PRD §8 数据模型）。</summary>
public class LaunchItem
{
    public string Name { get; set; } = "";

    /// <summary>启动目标：lnk/exe/目录/URL 原样保留（F4：Shell 代理解析）。</summary>
    public string Target { get; set; } = "";

    public string Args { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public ItemKind Kind { get; set; } = ItemKind.App;
    public bool RunAsAdmin { get; set; }

    /// <summary>图标缓存文件名（icons/ 目录下，目标路径 MD5 键名）。</summary>
    public string IconCache { get; set; } = "";

    // v1.9：使用频率排序
    /// <summary>累计启动次数。</summary>
    public int LaunchCount { get; set; }

    /// <summary>最后启动时间（UTC ticks，0 = 从未）。</summary>
    public long LastLaunchTicks { get; set; }
}
