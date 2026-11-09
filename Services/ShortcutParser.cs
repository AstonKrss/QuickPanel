using System.IO;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// 拖拽文件归一化为 LaunchItem（PRD F3/F4）。
/// .lnk 不做内部二进制解析，Target 保留 lnk 路径，由 Shell（UseShellExecute）代为解析启动，
/// 兼容 UWP/带参/提权快捷方式。
/// </summary>
public static class ShortcutParser
{
    public static LaunchItem? Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (Directory.Exists(path))
        {
            return new LaunchItem
            {
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Target = path,
                Kind = ItemKind.Folder,
                WorkDir = path,
            };
        }

        if (!File.Exists(path)) return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path);
        return ext switch
        {
            ".lnk" => new LaunchItem
            {
                Name = name,
                Target = path,
                Kind = ItemKind.App,
                WorkDir = Path.GetDirectoryName(path) ?? "",
            },
            ".url" => ParseUrl(path, name),
            ".exe" => new LaunchItem
            {
                Name = name,
                Target = path,
                Kind = ItemKind.App,
                WorkDir = Path.GetDirectoryName(path) ?? "",
            },
            _ => new LaunchItem { Name = name, Target = path, Kind = ItemKind.Document },
        };
    }

    public static List<LaunchItem?> ParseMany(IEnumerable<string> paths)
        => paths.Select(Parse).ToList();

    private static LaunchItem? ParseUrl(string path, string name)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) continue;
                var url = line[4..].Trim();
                if (url.Length == 0) return null;
                return new LaunchItem { Name = name, Target = url, Kind = ItemKind.Url };
            }
        }
        catch
        {
            // 非标准 .url 文件，放弃解析
        }
        return null;
    }
}
