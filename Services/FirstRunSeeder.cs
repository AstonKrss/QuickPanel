using System.IO;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>首次运行播种：桌面扫描 + 关键词分类进五分组（PRD F43）。</summary>
public static class FirstRunSeeder
{
    private static readonly (string Id, string Name, string[] Keywords)[] GroupDefs =
    {
        ("sec", "网安", new[]
        {
            "burp", "yakit", "antsword", "蚁剑", "010", "nmap", "sqlmap", "metasploit", "msf",
            "cobalt", "菜刀", "冰蝎", "哥斯拉", "behinder", "godzilla", "xray", "awvs", "nessus",
            "goby", "fscan", "kscan", "ida", "x64dbg", "ollydbg", "ghidra", "wireshark",
            "fiddler", "charles", "hydra", "hashcat", "wappalyzer", "hackbar", "proxifier",
            "portswigger", "dvwa", "pikachu", "vulhub",
        }),
        ("dev", "开发", new[]
        {
            "visual studio", "vscode", "code", "cursor", "idea", "datagrip", "pycharm",
            "webstorm", "rider", "goland", "clion", "android studio", "git", "sourcetree",
            "tortoisegit", "gitkraken", "bash", "wsl", "docker", "podman", "postman",
            "apifox", "apidog", "insomnia", "terminal", "powershell", "cmder",
            "tabby", "mobaxterm", "xshell", "finalshell", "securecrt", "navicat", "dbeaver",
            "heidisql", "redis", "mysql", "mongodb",
        }),
        ("game", "游戏", new[]
        {
            "steam", "epic", "minecraft", "pcl", "hmcl", "mc launcher", "battle.net",
            "battlenet", "战网", "origin", "ea app", "uplay", "ubisoft", "riot client",
            "league", "英雄联盟", "wegame", "valorant", "无畏契约", "dota", "genshin",
            "原神", "star rail", "崩坏", "mihoyo", "hoyoplay", "xbox",
        }),
        ("life", "日常", new[]
        {
            "wechat", "weixin", "微信", "wxwork", "企业微信", "qq", "tim", "dingtalk",
            "钉钉", "feishu", "飞书", "lark", "telegram", "discord", "netease", "网易云",
            "cloudmusic", "qq音乐", "qqmusic", "kugou", "kuwo", "bilibili", "哔哩",
            "iqiyi", "爱奇艺", "youku", "优酷", "腾讯视频", "spotify", "foxmail",
            "邮箱大师", "thunder", "迅雷", "百度网盘", "baidunetdisk", "阿里云盘",
            "aliyundrive", "夸克", "quark", "腾讯会议", "wemeet", "zoom", "腾讯文档",
            "notion", "evernote", "印象笔记",
        }),
        ("tools", "工具", new[]
        {
            "everything", "7-zip", "7zip", "winrar", "bandizip", "360zip", "notepad++",
            "notepad3", "typora", "marktext", "snipaste", "sharex", "picgo", "potplayer",
            "mpc", "vlc", "ditto", "listary", "wox", "obs", "vmware", "virtualbox",
            "ventoy", "rufus", "diskgenius", "aida64", "cpu-z", "gpu-z", "crystaldiskinfo",
            "trafficmonitor", "traffic monitor", "ccleaner", "geek uninstaller", "aimp", "foobar",
        }),
    };

    private static readonly string[] DockPriorityKeywords =
    {
        // F43：Dock 播种常用 6 项（按优先级命中）
        "wechat", "weixin", "微信", "visual studio code", "vscode", "everything",
        "chrome", "msedge", "edge", "firefox", "terminal", "终端", "explorer",
        "文件资源管理器", "potplayer", "notepad++",
    };

    public static void Seed(AppConfig config)
    {
        config.Groups = GroupDefs
            .Select(d => new Group { Id = d.Id, Name = d.Name, DockPinned = true })
            .ToList();

        var desktops = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<LaunchItem>();
        foreach (var dir in desktops.Where(Directory.Exists))
        {
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk"))
            {
                var item = ShortcutParser.Parse(lnk);
                if (item is null || !seen.Add(item.Target)) continue;
                items.Add(item);
            }
        }

        foreach (var item in items)
        {
            var groupId = Classify($"{item.Name} {Path.GetFileName(item.Target)}");
            config.Groups.First(g => g.Id == groupId).Items.Add(item);
        }

        config.DockPins = SeedDockPins(items);
    }

    public static string Classify(string text)
    {
        return TryClassify(text) ?? "tools"; // 未命中归入工具组
    }

    /// <summary>
    /// v1.23：尝试按关键词分类，未命中返回 null（用于区分"确实属于工具"与"兜底归入工具"）。
    /// </summary>
    public static string? TryClassify(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var def in GroupDefs)
        {
            if (def.Keywords.Any(lower.Contains))
                return def.Id;
        }
        return null;
    }

    private static List<LaunchItem> SeedDockPins(List<LaunchItem> items)
    {
        var pins = new List<LaunchItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in DockPriorityKeywords)
        {
            var hit = items.FirstOrDefault(i => !used.Contains(i.Target)
                && i.Name.ToLowerInvariant().Contains(keyword));
            if (hit is null) continue;
            pins.Add(hit);
            used.Add(hit.Target);
            if (pins.Count >= 6) break;
        }
        return pins;
    }
}
