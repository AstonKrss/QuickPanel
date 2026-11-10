using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickPanel.Services;

/// <summary>
/// v1.16：高分辨率图标提取 —— IShellItemImageFactory 取 96px 缩略图。
/// 相比 SHGetFileInfo 的 32px，在高 DPI / 图标放大（Dock 悬停 1.32x）下清晰得多。
/// 失败时回退到 SHGetFileInfo（32px），再失败回退首字母。
/// </summary>
internal static class ShellIconFactory
{
    private const uint SIIGBF_RESIZETOFIT = 0x00;
    private const uint SIIGBF_BIGGERSIZEOK = 0x01;
    private const uint SIIGBF_ICONONLY = 0x04;

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>取图标位图（最多 96px）。失败返回 null。</summary>
    public static BitmapSource? TryGetIcon(string path, int size = 96)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        object? item = null;
        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item);
            if (item is not IShellItemImageFactory factory) return null;

            var sz = new SIZE { cx = size, cy = size };
            var hr = factory.GetImage(sz, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbmp);
            if (hr != 0 || hbmp == IntPtr.Zero) return null;

            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                    hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // 冻结后可跨线程使用，避免动画期间反复拷贝
                var frozen = bmp.Clone();
                frozen.Freeze();
                return frozen;
            }
            finally
            {
                DeleteObject(hbmp);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (item != null && Marshal.IsComObject(item))
                Marshal.ReleaseComObject(item);
        }
    }
}
