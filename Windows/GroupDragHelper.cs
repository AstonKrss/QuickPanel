using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickPanel.ViewModels;

namespace QuickPanel.Windows;

/// <summary>
/// v1.13：ListBox 分组拖拽排序辅助。
/// 用法：ListBoxItem 上挂 PreviewMouseLeftButtonDown / MouseMove / Drop。
/// </summary>
public static class GroupDragHelper
{
    private static Point _start;
    private static GroupVM? _dragged;

    /// <summary>开始拖拽检测（按下时调用）。</summary>
    public static void OnDown(ListBox list, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GroupVM g) return;
        _dragged = g;
        _start = e.GetPosition(list);
    }

    /// <summary>移动时若超过阈值则启动 DragDrop。调用方需把 GroupVM 放进 DataObject。</summary>
    public static bool TryStartDrag(ListBox list, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed) return false;
        var pos = e.GetPosition(list);
        if (Math.Abs(pos.X - _start.X) + Math.Abs(pos.Y - _start.Y)
            < SystemParameters.MinimumVerticalDragDistance) return false;

        var dragged = _dragged;
        _dragged = null;
        try
        {
            var data = new DataObject("QuickPanel.Group", dragged);
            DragDrop.DoDragDrop(list, data, DragDropEffects.Move);
        }
        catch { /* 拖拽取消 */ }
        return true;
    }

    /// <summary>拖到上方时插入到目标索引。</summary>
    public static void OnDrop(ListBox list, DragEventArgs e, Action<GroupVM, int> move)
    {
        if (e.Data.GetData("QuickPanel.Group") is not GroupVM dragged) return;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (target?.DataContext is not GroupVM targetGroup) return;

        var from = list.Items.IndexOf(dragged);
        var to = list.Items.IndexOf(targetGroup);
        if (from < 0 || to < 0 || from == to) return;

        // 拖到目标上半 → 插到目标前；下半 → 插到目标后
        var pos = e.GetPosition(target);
        if (pos.Y > target.ActualHeight / 2 && to < list.Items.Count - 1) to++;

        move(dragged, to);
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T hit) return hit;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
