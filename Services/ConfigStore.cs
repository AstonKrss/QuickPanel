using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>配置加载/保存/备份/迁移（PRD F40/F41/F44、§5.4 配置自愈）。</summary>
public class ConfigStore
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPanel");

    public static string ConfigPath { get; } = Path.Combine(DataDir, "config.json");
    public static string IconsDir { get; } = Path.Combine(DataDir, "icons");
    public static string BackupDir { get; } = Path.Combine(DataDir, "backup");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 兼容读取旧版 PascalCase 配置（用户数据无损迁移），保存时统一为 camelCase（§8）
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>首次运行（无配置文件或配置损坏自愈）。</summary>
    public bool FirstRun { get; private set; }

    public AppConfig Config { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(IconsDir);
        Directory.CreateDirectory(BackupDir);

        if (!File.Exists(ConfigPath))
        {
            FirstRun = true;
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(ConfigPath);
        }
        catch (IOException)
        {
            // v1.7：IO 异常（文件被占用/权限/磁盘）不是配置损坏 —— 绝不能覆盖用户数据
            throw;
        }

        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // 仅 JSON 语法/结构错误才自愈；先把坏文件留档，避免用户数据无声丢失
            try
            {
                var bad = Path.Combine(DataDir, $"config.bad.{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Copy(ConfigPath, bad, overwrite: true);
            }
            catch { /* 留档失败不阻塞自愈 */ }
            FirstRun = true;
            Config = new AppConfig();
        }

        Normalize();
        Migrate();
    }

    /// <summary>v1.7：反序列化后归一化 null 集合，避免后续 NRE。</summary>
    private void Normalize()
    {
        Config.Settings ??= new AppSettings();
        Config.Groups ??= new List<Group>();
        Config.DockPins ??= new List<LaunchItem>();
        Config.Groups = Config.Groups.Where(g => g != null).ToList();
        foreach (var g in Config.Groups)
        {
            g.Items ??= new List<LaunchItem>();
            g.Items = g.Items.Where(i => i != null).ToList();
            if (string.IsNullOrEmpty(g.Id)) g.Id = Guid.NewGuid().ToString("N")[..8];
            if (string.IsNullOrEmpty(g.Name)) g.Name = "未命名";
        }
        Config.DockPins = Config.DockPins.Where(p => p != null).ToList();
    }

    private void Migrate()
    {
        // F44：旧热键 Alt+Space 会被系统窗口菜单占用，自动迁移为 Alt+Q
        if (string.Equals(Config.Settings.Hotkey, "Alt+Space", StringComparison.OrdinalIgnoreCase))
            Config.Settings.Hotkey = "Alt+Q";
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        var json = JsonSerializer.Serialize(Config, JsonOpts);
        // v1.7：原子写入（先写 tmp 再替换），避免断电/崩溃留下截断文件
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(ConfigPath))
            File.Replace(tmp, ConfigPath, null);
        else
            File.Move(tmp, ConfigPath);
    }

    /// <summary>F41：一键备份，时间戳文件落 backup/。</summary>
    public string Backup()
    {
        Directory.CreateDirectory(BackupDir);
        var path = Path.Combine(BackupDir, $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Config, JsonOpts));
        return path;
    }
}
