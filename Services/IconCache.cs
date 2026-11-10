using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPanel.Models;

namespace QuickPanel.Services;

/// <summary>图标提取 → PNG 落盘缓存 → 冻结位图（PRD F5）。</summary>
public class IconCache
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x00000100;
    private const uint SHGFI_LARGEICON = 0x00000000;

    /// <summary>取图标：缓存命中 → SHGetFileInfo 提取 → 失败回退首字母生成。</summary>
    /// <summary>v1.16：缓存文件名带尺寸前缀，旧 32px 缓存自动失效并重提 96px。</summary>
    public BitmapSource? GetIcon(LaunchItem item)
    {
        var fileName = Path.ChangeExtension("hq_" + Md5Hex(item.Target), ".png");
        item.IconCache = fileName;

        var cached = LoadCached(fileName);
        if (cached != null) return cached;

        // v1.16：优先 IShellItemImageFactory 取 96px 高清图标
        var bitmap = ShellIconFactory.TryGetIcon(item.Target, 96)
                     ?? ExtractIcon(item.Target)      // 回退 32px
                     ?? GenerateFallback(item.Name);  // 回退首字母
        SavePng(bitmap, Path.Combine(ConfigStore.IconsDir, fileName));
        return bitmap;
    }

    private static BitmapSource? LoadCached(string fileName)
    {
        try
        {
            var path = Path.Combine(ConfigStore.IconsDir, fileName);
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? ExtractIcon(string target)
    {
        try
        {
            var info = new SHFILEINFOW();
            var res = SHGetFileInfo(target, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>提取失败兜底：首字母 + 主题强调色（PRD F5）。</summary>
    private static BitmapSource GenerateFallback(string name)
    {
        var letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
        const int size = 44;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0x7C, 0x6F, 0xFF)), null,
                new Rect(0, 0, size, size), 10, 10);
            var text = new FormattedText(
                letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 22, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }
        catch
        {
            // 缓存写失败不影响本次会话，二次启动会重试
        }
    }

    private static string Md5Hex(string text)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
