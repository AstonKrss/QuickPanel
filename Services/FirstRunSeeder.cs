using System.IO;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>
/// 首次运行播种：桌面扫描 + 关键词分类。
/// v1.24：分类改为通用 6 类（去掉特定人群的"网安"），并加入厂商/品牌识别。
/// </summary>
public static class FirstRunSeeder
{
    private static readonly (string Id, string Name, string[] Keywords)[] GroupDefs =
    {
        // 开发 —— IDE / 终端 / 数据库 / 版本控制
        ("dev", "开发", new[]
        {
            "visual studio", "vscode", "code", "cursor", "idea", "datagrip", "pycharm",
            "webstorm", "rider", "goland", "clion", "android studio", "sublime",
            "git", "sourcetree", "tortoisegit", "gitkraken", "github desktop",
            "bash", "wsl", "docker", "podman", "postman", "apifox", "apidog", "insomnia",
            "terminal", "powershell", "cmd", "cmder", "tabby", "mobaxterm", "xshell",
            "finalshell", "securecrt", "navicat", "dbeaver", "heidisql", "datagrip",
            "redis", "mysql", "mongodb", "python", "node", "java", "jdk", "anaconda",
            "burp", "yakit", "antsword", "蚁剑", "010", "nmap", "ida", "x64dbg", "ghidra",
            "wireshark", "fiddler", "charles", "jmeter", "phpstudy", "vsc",
        }),
        // 办公 —— 文档 / 表格 / 演示 / 笔记
        ("office", "办公", new[]
        {
            "word", "excel", "powerpoint", "ppt", "onenote", "outlook", "office",
            "libreoffice", "wps", "acrobat", "foxit", "pdf", "typora", "marktext",
            "notion", "evernote", "印象笔记", "obsidian", "xmind", "mindmaster",
            "石墨", "腾讯文档", "飞书文档", "钉钉文档", "onedrive", "坚果云", "nutstore",
            "adobe", "photoshop", "illustrator", "after effects", "premiere", "figma",
            "sketch", "axure", "墨刀", "canva", "剪映", "capcut",
        }),
        // 影音 —— 播放器 / 音乐 / 视频平台
        ("media", "影音", new[]
        {
            "potplayer", "vlc", "mpc", "kmplayer", "obs", "foobar", "aimp",
            "网易云", "cloudmusic", "qq音乐", "qqmusic", "kugou", "酷狗", "kuwo", "酷我",
            "spotify", "bilibili", "哔哩", "iqiyi", "爱奇艺", "youku", "优酷",
            "腾讯视频", "芒果", "mgtv", "netflix", "youtube", "抖音", "快手",
            "audition", "audacity", "格式工厂", "handbrake",
        }),
        // 游戏 —— 平台 / 启动器
        ("game", "游戏", new[]
        {
            "steam", "epic", "minecraft", "pcl", "hmcl", "mc launcher", "battle.net",
            "battlenet", "战网", "origin", "ea app", "uplay", "ubisoft", "riot client",
            "league", "英雄联盟", "wegame", "valorant", "无畏契约", "dota", "genshin",
            "原神", "star rail", "崩坏", "mihoyo", "hoyoplay", "xbox", "uu加速", "uu远程",
        }),
        // 社交 —— 即时通讯 / 会议
        ("social", "社交", new[]
        {
            "wechat", "weixin", "微信", "wxwork", "企业微信", "qq", "tim",
            "dingtalk", "钉钉", "feishu", "飞书", "lark", "telegram", "discord",
            "whatsapp", "line", "skype", "zoom", "腾讯会议", "wemeet", "voov",
            "teams", "slack", "腾讯", "tencent", "网易", "netease",
        }),
        // 工具 —— 系统增强 / 压缩 / 截图 / 虚拟机
        ("tools", "工具", new[]
        {
            "everything", "listary", "wox", "ditto", "7-zip", "7zip", "winrar",
            "bandizip", "360zip", "好压", "2345", "notepad++", "notepad3",
            "snipaste", "sharex", "picgo", "vmware", "virtualbox", "ventoy", "rufus",
            "diskgenius", "分区助手", "aida64", "cpu-z", "gpu-z", "crystaldiskinfo",
            "trafficmonitor", "traffic monitor", "ccleaner", "geek uninstaller",
            "火绒", "huorong", "360安全", "腾讯电脑管家", "驱动", "driver",
            "nvidia", "amd", "intel", "realtek", "微软", "microsoft",
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
