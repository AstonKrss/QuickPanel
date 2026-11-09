namespace QuickPanel.Models;

/// <summary>分组（PRD §8 数据模型）。</summary>
public class Group
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool DockPinned { get; set; } = true;
    public List<LaunchItem> Items { get; set; } = new();
}
