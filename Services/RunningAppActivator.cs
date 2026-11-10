using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickPanel.Services;

/// <summary>
/// v1.31：已运行的软件直接切窗口（与任务栏点击行为一致）。
/// 启动一个新进程要几百毫秒（Electron 之类还要走单实例握手），
/// 而任务栏点击只是 SetForegroundWindow —— 这就是"从面板打开更慢"的主因。
/// </summary>
public static class RunningAppActivator
{
    private const int SW_RESTORE = 9;

    private static readonly Dictionary<string, string?> Resolved = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // 前台锁偶尔会拒绝 SetForegroundWindow；SwitchToThisWindow 是备选（任务栏同源）
    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    /// <summary>找到已在运行的实例并切到前台。成功返回 true（调用方应跳过启动）。</summary>
    public static bool TryActivate(string target)
    {
        var exe = ResolveExecutable(target);
        if (exe is null) return false;

        var name = Path.GetFileNameWithoutExtension(exe);
        if (string.IsNullOrEmpty(name)) return false;

        // 资源管理器/外壳类目标不能按进程"复用"，否则会聚焦到任意一个窗口
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;

        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch { return false; }

        foreach (var process in processes)
        {
            try
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;
                if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                if (SetForegroundWindow(handle)) return true;
                SwitchToThisWindow(handle, true);
                if (GetForegroundWindow() == handle) return true;
            }
            catch { /* 权限/进程退出：跳过 */ }
            finally { process.Dispose(); }
        }
        return false;
    }

    /// <summary>把 .lnk/.exe 解析成 exe 路径（结果缓存，首启一次开销）。</summary>
    private static string? ResolveExecutable(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        if (Resolved.TryGetValue(target, out var cached)) return cached;

        string? result = null;
        try
        {
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                result = target;
            }
            else if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is not null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(target);
                    string path = (string)shortcut.TargetPath;
                    if (!string.IsNullOrWhiteSpace(path)
                        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(path))
                    {
                        result = path;
                    }
                }
            }
        }
        catch { result = null; }

        Resolved[target] = result;
        return result;
    }
}
