using System.IO;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>扫描到的候选程序（供初始化向导勾选）。</summary>
public class ScannedApp
{
    public required LaunchItem Item { get; init; }
    /// <summary>自动分类到的分组 Id。</summary>
    public required string GroupId { get; init; }
    /// <summary>来源：桌面 / 开始菜单。</summary>
    public required string Source { get; init; }

    /// <summary>是否按关键词明确命中分组（false = 兜底归入工具组）。</summary>
    public bool ConfidentMatch { get; init; }

    /// <summary>默认是否勾选：只勾"明确命中"的，兜底项默认不勾，避免一次导入上百个系统工具。</summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// v1.23：应用扫描器 —— 扫描桌面与开始菜单的快捷方式，按关键词分类。
/// 供初始化向导与"重新扫描"使用。
/// </summary>
public static class AppScanner
{
    /// <summary>扫描桌面 + 开始菜单，返回去重后的候选列表（已分类）。</summary>
    public static List<ScannedApp> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScannedApp>();

        foreach (var (dir, source) in EnumerateScanDirs())
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly);
            }
            catch { continue; }

            foreach (var lnk in files)
            {
                ItemEntry(result, seen, lnk, source);
            }
        }

        // 排序：先按分组顺序，再按名称
        var groupOrder = new Dictionary<string, int>
        {
            ["sec"] = 0, ["dev"] = 1, ["life"] = 2, ["game"] = 3, ["tools"] = 4,
        };
        return result
            .OrderBy(a => groupOrder.TryGetValue(a.GroupId, out var i) ? i : 99)
            .ThenBy(a => a.Item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void ItemEntry(List<ScannedApp> result, HashSet<string> seen, string lnk, string source)
    {
        try
        {
            var item = ShortcutParser.Parse(lnk);
            if (item is null) return;

            // v1.23：去重键用"显示名"而不是 Target ——
            // .lnk 的 Target 是 lnk 自身路径，桌面版与开始菜单版路径不同，会漏判重复。
            var key = item.Name.Trim().ToLowerInvariant();
            if (!seen.Add(key)) return;

            var matched = FirstRunSeeder.TryClassify($"{item.Name} {Path.GetFileName(item.Target)}");
            var groupId = matched ?? "tools";
            result.Add(new ScannedApp
            {
                Item = item,
                GroupId = groupId,
                Source = source,
                ConfidentMatch = matched != null,
                IsSelected = matched != null, // 兜底项默认不勾
            });
        }
        catch { /* 单个快捷方式解析失败不影响整体 */ }
    }

    private static IEnumerable<(string Dir, string Source)> EnumerateScanDirs()
    {
        // 桌面
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "桌面");

        // 开始菜单（用户 + 所有用户），含一层子目录
        var startMenus = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };
        foreach (var sm in startMenus.Where(Directory.Exists))
        {
            yield return (Path.Combine(sm, "Programs"), "开始菜单");
            // Programs 下常见一层子文件夹（如「开发工具」）
            string[] subs;
            try { subs = Directory.GetDirectories(Path.Combine(sm, "Programs")); }
            catch { subs = Array.Empty<string>(); }
            foreach (var sub in subs)
                yield return (sub, "开始菜单");
        }
    }

    /// <summary>取分组显示名（用于向导中的分类标题）。</summary>
    public static string GroupDisplayName(string groupId) => groupId switch
    {
        "sec" => "网安",
        "dev" => "开发",
        "life" => "日常",
        "game" => "游戏",
        _ => "工具",
    };
}
