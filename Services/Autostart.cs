using Microsoft.Win32;

namespace QuickPanel.Services;

/// <summary>开机自启（PRD F36/F42：设置窗口 CheckBox → HKCU Run 键）。</summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickPanel";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        key.SetValue(ValueName, $"\"{exe}\"");
    }
}
