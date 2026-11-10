using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>启动三类目标 + UAC 提权（PRD F6/F7、§5.2 提权边界）。</summary>
public class LaunchService
{
    /// <summary>reuseRunning=true 时，若目标已在运行则直接切窗口（快得多）。</summary>
    public void Launch(LaunchItem item, bool asAdmin = false, bool reuseRunning = false)
    {
        if (reuseRunning && !asAdmin && !item.RunAsAdmin
            && RunningAppActivator.TryActivate(item.Target))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Target,
                Arguments = item.Args,
                UseShellExecute = true, // F4：Shell 代理解析（lnk/UWP/带参/提权）
                WorkingDirectory = ResolveWorkDir(item),
            };
            if (asAdmin || item.RunAsAdmin)
                psi.Verb = "runas"; // 正规 UAC，不绕过、不静默提权（§5.2）
            Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：用户取消 UAC，静默
        }
        catch (Exception ex)
        {
            // F6：目标不存在等异常给出提示
            MessageBox.Show(
                $"无法启动「{item.Name}」\n目标：{item.Target}\n\n{ex.Message}",
                "QuickPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string ResolveWorkDir(LaunchItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.WorkDir) && Directory.Exists(item.WorkDir))
            return item.WorkDir;
        if (File.Exists(item.Target))
            return Path.GetDirectoryName(item.Target) ?? "";
        return "";
    }
}
