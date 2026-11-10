using System.Diagnostics;

namespace QuickPanel.Services;

/// <summary>v1.31：外部链接（仅在用户点击时打开，运行期不做任何网络请求）。</summary>
public static class Links
{
    public const string Author = "Aston";
    public const string RepoUrl = "https://github.com/AstonKrss/QuickPanel";

    public static void OpenRepo()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepoUrl, UseShellExecute = true });
        }
        catch { /* 打不开浏览器不影响程序 */ }
    }
}
