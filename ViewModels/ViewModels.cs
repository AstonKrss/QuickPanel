using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using QuickPanel.Models;
using QuickPanel.Services;

namespace QuickPanel.ViewModels;

/// <summary>条目视图模型：持有 Model + 已解析图标。</summary>
public class ItemVM : INotifyPropertyChanged
{
    public LaunchItem Model { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? Icon { get; }

    /// <summary>所属分组（搜索结果副标题与移动分组用；Dock 钉选副本为 null）。</summary>
    public GroupVM? OwnerGroup { get; set; }

    public ItemVM(LaunchItem model, IconCache icons)
    {
        Model = model;
        _name = model.Name;
        try
        {
            Icon = icons.GetIcon(model);
        }
        catch
        {
            // 图标提取失败不影响条目本身
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>分组视图模型。</summary>
public class GroupVM : INotifyPropertyChanged
{
    public Group Model { get; }
    public ObservableCollection<ItemVM> Items { get; } = new();

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public bool DockPinned
    {
        get => Model.DockPinned;
        set
        {
            if (Model.DockPinned == value) return;
            Model.DockPinned = value;
            OnPropertyChanged();
        }
    }

    public GroupVM(Group model, IconCache icons)
    {
        Model = model;
        foreach (var item in model.Items)
        {
            var vm = new ItemVM(item, icons) { OwnerGroup = this };
            Items.Add(vm);
        }
    }

    public void AddItem(ItemVM item)
    {
        item.OwnerGroup = this;
        Items.Add(item);
        Model.Items.Add(item.Model);
    }

    public void RemoveItem(ItemVM item)
    {
        // v1.7：先确认真的在这个分组里，再清 OwnerGroup（否则搜索结果会误清归属）
        if (!Items.Remove(item)) return;
        Model.Items.Remove(item.Model);
        item.OwnerGroup = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>主视图模型：分组、Dock 钉选与全部条目操作。</summary>
public class MainViewModel : INotifyPropertyChanged
{
    public ConfigStore Store { get; }
    public IconCache Icons { get; } = new();
    public LaunchService Launcher { get; } = new();

    public ObservableCollection<GroupVM> Groups { get; } = new();
    public ObservableCollection<ItemVM> DockPins { get; } = new();

    private GroupVM? _selectedGroup;
    public GroupVM? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_selectedGroup == value) return;
            _selectedGroup = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel(ConfigStore store) => Store = store;

    public void LoadFromConfig()
    {
        if (Store.FirstRun || Store.Config.Groups.Count == 0)
            FirstRunSeeder.Seed(Store.Config);

        // F44：非首运但 Dock 钉选为空时自动播种（v1.7：仅首次，用户清空后不再回填）
        if (!Store.FirstRun && !Store.Config.Settings.DockSeeded && Store.Config.DockPins.Count == 0)
        {
            foreach (var item in Store.Config.Groups.SelectMany(g => g.Items).Take(6))
                Store.Config.DockPins.Add(item);
        }
        // 已有钉选或已完成播种 → 标记，避免清空后重启又被填回
        if (Store.Config.DockPins.Count > 0 || Store.FirstRun)
            Store.Config.Settings.DockSeeded = true;

        foreach (var group in Store.Config.Groups)
            Groups.Add(new GroupVM(group, Icons));
        foreach (var pin in Store.Config.DockPins)
            DockPins.Add(new ItemVM(pin, Icons));

        SelectedGroup = Groups.FirstOrDefault();
    }

    public void SaveConfig()
    {
        Store.Config.Groups = Groups.Select(g => g.Model).ToList();
        Store.Config.DockPins = DockPins.Select(p => p.Model).ToList();
        Store.Save();
    }

    // ---------- 条目 ----------

    public void LaunchItem(ItemVM item, bool asAdmin = false)
    {
        // v1.9：记录使用频率（用于搜索排序）
        try
        {
            item.Model.LaunchCount++;
            item.Model.LastLaunchTicks = DateTime.UtcNow.Ticks;
            SaveConfig();
        }
        catch { /* 统计失败不影响启动 */ }
        Launcher.Launch(item.Model, asAdmin);
    }

    /// <summary>F3：拖入文件加入当前分组并立即持久化（v1.7：同目标去重）。</summary>
    public void AddToCurrentGroup(IEnumerable<string> paths)
    {
        var group = SelectedGroup;
        if (group is null) return;
        foreach (var path in paths)
        {
            var item = ShortcutParser.Parse(path);
            if (item is null) continue;
            if (group.Items.Any(i => string.Equals(i.Model.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
                continue; // v1.7：重复拖入不再产生重复条目
            group.AddItem(new ItemVM(item, Icons));
        }
        SaveConfig();
    }

    /// <summary>v1.7：手动添加条目（文件/文件夹/网址）。</summary>
    public bool AddItem(string name, string target, ItemKind kind)
    {
        var group = SelectedGroup;
        if (group is null || string.IsNullOrWhiteSpace(target)) return false;
        if (group.Items.Any(i => string.Equals(i.Model.Target, target, StringComparison.OrdinalIgnoreCase)))
            return false;

        var model = new LaunchItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? target : name.Trim(),
            Target = target.Trim(),
            Kind = kind,
            WorkDir = File.Exists(target) ? (Path.GetDirectoryName(target) ?? "") : "",
        };
        group.AddItem(new ItemVM(model, Icons));
        SaveConfig();
        return true;
    }

    public void RemoveFromCurrentGroup(ItemVM item)
    {
        SelectedGroup?.RemoveItem(item);
        SaveConfig();
    }

    /// <summary>v1.7：按条目真实归属移除（搜索结果场景）。</summary>
    public void RemoveFromOwnGroup(ItemVM item)
    {
        item.OwnerGroup?.RemoveItem(item);
        SaveConfig();
    }

    // ---------- Dock（F22 + v0.6 固定/取消）----------

    /// <summary>钉选到 Dock。返回是否有新增（v1.12：供 UI 判断重复/上限）。</summary>
    public bool PinToDock(IEnumerable<string> paths)
    {
        const int maxPins = 14;
        var added = false;
        foreach (var path in paths)
        {
            var item = ShortcutParser.Parse(path);
            if (item is null) continue;
            if (DockPins.Any(p => string.Equals(p.Model.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
                continue; // 防重复
            if (DockPins.Count >= maxPins)
            {
                MessageBox.Show($"Dock 钉选已达上限 {maxPins} 个，请先取消部分钉选。",
                    "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            DockPins.Add(new ItemVM(item, Icons));
            added = true;
        }
        if (added) SaveConfig();
        return added;
    }

    public void Unpin(ItemVM item)
    {
        DockPins.Remove(item);
        SaveConfig();
    }

    /// <summary>v1.8：Dock 钉选重命名。</summary>
    public void RenamePin(ItemVM item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        item.Name = newName.Trim();
        SaveConfig();
    }

    /// <summary>v1.8：Dock 钉选排序（左移/右移）。</summary>
    public void MovePin(ItemVM item, int delta)
    {
        var i = DockPins.IndexOf(item);
        if (i < 0) return;
        var j = i + delta;
        if (j < 0 || j >= DockPins.Count) return;
        DockPins.Move(i, j);
        SaveConfig();
    }

    /// <summary>v1.8：Dock 钉选置顶到最左。</summary>
    public void MovePinToFront(ItemVM item)
    {
        var i = DockPins.IndexOf(item);
        if (i <= 0) return;
        DockPins.Move(i, 0);
        SaveConfig();
    }

    /// <summary>v1.8：编辑 Dock 钉选的启动参数/工作目录/管理员标记。</summary>
    public void UpdatePin(ItemVM item, string args, string workDir, bool runAsAdmin)
    {
        item.Model.Args = args ?? "";
        item.Model.WorkDir = workDir ?? "";
        item.Model.RunAsAdmin = runAsAdmin;
        SaveConfig();
    }

    public bool IsPinned(ItemVM item)
        => DockPins.Any(p => string.Equals(p.Model.Target, item.Model.Target, StringComparison.OrdinalIgnoreCase));

    /// <summary>v0.6：面板条目固定/取消固定到 Dock（钉选使用独立副本，不与分组条目共享实例）。</summary>
    public void TogglePin(ItemVM item)
    {
        var existing = DockPins.FirstOrDefault(p =>
            string.Equals(p.Model.Target, item.Model.Target, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            DockPins.Remove(existing);
            SaveConfig();
            return;
        }
        if (DockPins.Count >= 14)
        {
            MessageBox.Show("Dock 钉选已达上限 14 个，请先取消部分钉选。",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var clone = new LaunchItem
        {
            Name = item.Model.Name,
            Target = item.Model.Target,
            Args = item.Model.Args,
            WorkDir = item.Model.WorkDir,
            Kind = item.Model.Kind,
            RunAsAdmin = item.Model.RunAsAdmin,
            IconCache = item.Model.IconCache,
        };
        DockPins.Add(new ItemVM(clone, Icons));
        SaveConfig();
    }

    /// <summary>v0.6：把条目移动到其它分组。</summary>
    public void MoveItem(ItemVM item, GroupVM target)
    {
        if (ReferenceEquals(item.OwnerGroup, target)) return;
        item.OwnerGroup?.RemoveItem(item);
        target.AddItem(item);
        SaveConfig();
    }

    /// <summary>v1.14：分组内条目排序。</summary>
    public void MoveItemWithinGroup(GroupVM group, ItemVM item, int targetIndex)
    {
        var from = group.Items.IndexOf(item);
        if (from < 0) return;
        var to = Math.Clamp(targetIndex, 0, group.Items.Count - 1);
        if (from == to) return;

        group.Items.Move(from, to);
        // 同步 Model.Items 顺序
        group.Model.Items.Remove(item.Model);
        group.Model.Items.Insert(to, item.Model);
        SaveConfig();
    }

    /// <summary>
    /// F60 v0.6：跨分组搜索。
    /// v1.9：支持拼音首字母（wx→微信）+ 按使用频率/最近使用排序。
    /// </summary>
    public List<ItemVM> SearchAll(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return new List<ItemVM>();
        return Groups.SelectMany(g => g.Items)
            .Where(i => PinyinIndex.Matches(i.Name, q))
            .OrderByDescending(i => i.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.Model.LaunchCount)          // v1.9：常用优先
            .ThenByDescending(i => i.Model.LastLaunchTicks)      // v1.9：最近使用优先
            .Take(40)
            .ToList();
    }

    // ---------- 分组管理（F9 基础版）----------

    public void AddGroup(string name)
    {
        var group = new GroupVM(new Group { Id = Guid.NewGuid().ToString("N")[..8], Name = name }, Icons);
        Groups.Add(group);
        SelectedGroup = group;
        SaveConfig();
    }

    public void RemoveGroup(GroupVM group)
    {
        Groups.Remove(group);
        if (SelectedGroup == group)
            SelectedGroup = Groups.FirstOrDefault();
        SaveConfig();
    }

    /// <summary>v1.13：拖拽排序 —— 把分组移动到指定索引。</summary>
    public void MoveGroup(GroupVM group, int targetIndex)
    {
        var from = Groups.IndexOf(group);
        if (from < 0) return;
        var to = Math.Clamp(targetIndex, 0, Groups.Count - 1);
        if (from == to) return;
        Groups.Move(from, to);
        SaveConfig();
    }

    /// <summary>v1.13：按光标位置计算目标索引并移动（供拖拽使用）。</summary>
    public void MoveGroupToPosition(GroupVM group, double cursorY, IList<GroupVM> ordered)
    {
        var target = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], group)) continue;
            target = i;
            break;
        }
        if (target >= 0) MoveGroup(group, target);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
