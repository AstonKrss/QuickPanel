using System.IO;
using Microsoft.Win32;

namespace QuickPanel.Services;

/// <summary>开机自启（PRD F36/F42：设置窗口 CheckBox → HKCU Run 键）。</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickPanel";

    /// <summary>
    /// v1.33：键值存在还不够 —— 目标 exe 已被删除/搬家时 Windows 会静默失败，
    /// 界面却仍显示"已开启"。这里额外校验文件存在，避免用户以为开了其实没开。
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(ValueName) is not string value || value.Length == 0) return false;

        var path = value.Trim().Trim('"');
        return path.Length > 0 && File.Exists(path);
    }

    /// <summary>设置开机自启；返回是否达成目标状态。</summary>
    public static bool SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        key.SetValue(ValueName, $"\"{exe}\"");
        return IsEnabled();
    }
}
