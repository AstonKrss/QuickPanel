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

        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts)
                     ?? new AppConfig();
        }
        catch
        {
            // §5.4 配置自愈：损坏自动重建默认并播种
            FirstRun = true;
            Config = new AppConfig();
        }

        Migrate();
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
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, JsonOpts));
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
