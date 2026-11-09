using System.IO;
using System.Runtime.InteropServices;

namespace QuickPanel.Services;

public enum AcrylicLevel
{
    /// <summary>Win11 DWM SystemBackdrop（首选）。</summary>
    Backdrop,
    /// <summary>AccentPolicy BLURBEHIND（Win10 兜底）。</summary>
    BlurBehind,
    /// <summary>纯色降级。</summary>
    Solid,
    /// <summary>无背板（AllowsTransparency 分层窗口，透出壁纸）。</summary>
    Transparent,
}

/// <summary>毛玻璃三级降级链（PRD F50 / §5.4），全程写 acrylic.log。</summary>
public static class Acrylic
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // 亚克力质感
    private const int DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attrib;
        public IntPtr pvData;
        public int cbData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    /// <summary>v1.0：对话框（设置/输入框）标题栏跟随主题。dark=true 深色标题栏。</summary>
    public static void ApplyDialogTheme(IntPtr hwnd, bool dark)
    {
        TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        TrySet(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);
        TrySet(hwnd, DWMWA_CAPTION_COLOR, unchecked((int)(dark ? 0x00181515 : 0x00FAF7F7))); // COLORREF BGR
    }

    /// <summary>
    /// 应用毛玻璃与圆角/深色/无边框，返回实际降级级别。useBackdrop=false 为全透明模式（无模糊背板）。
    /// 注意：不要在这里设置 DWMWA_CAPTION_COLOR —— DWM 会给窗口涂一层标题栏色块，
    /// 在分层透明窗口上表现为去不掉的圆角矩形"雾膜"（v1.1 踩坑，标题栏着色只用于对话框）。
    /// </summary>
    public static AcrylicLevel Apply(IntPtr hwnd, bool useBackdrop = true)
    {
        TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        TrySet(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);

        if (!useBackdrop)
        {
            Log("透明模式：无背板（AllowsTransparency 分层窗口）");
            return AcrylicLevel.Transparent;
        }

        var build = Environment.OSVersion.Version.Build;
        if (build >= 22621 && TrySet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, (uint)DWMSBT_TRANSIENTWINDOW))
        {
            Log($"L1 DWM SystemBackdrop=TransientWindow OK (build {build})");
            return AcrylicLevel.Backdrop;
        }
        Log($"L1 DWM SystemBackdrop 不可用 (build {build})");

        if (TryAccentBlur(hwnd))
        {
            Log("L2 AccentPolicy BLURBEHIND OK");
            return AcrylicLevel.BlurBehind;
        }
        Log("L3 纯色降级");
        return AcrylicLevel.Solid;
    }

    private static bool TrySet(IntPtr hwnd, int attr, int value)
        => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)) == 0;

    private static bool TrySet(IntPtr hwnd, int attr, uint value)
        => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(uint)) == 0;

    private static bool TryAccentBlur(IntPtr hwnd)
    {
        try
        {
            var policy = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 0,
                GradientColor = 0x01000000,
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attrib = WCA_ACCENT_POLICY,
                    pvData = ptr,
                    cbData = size,
                };
                return SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(ConfigStore.DataDir, "acrylic.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败静默
        }
    }
}
