using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuickPanel.Services;

/// <summary>全局热键注册/重注册/回退（PRD F30：RegisterHotKey + WM_HOTKEY，防重复挂钩）。</summary>
public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x5151;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _hookAttached;
    private bool _registered;

    public event Action? HotkeyPressed;

    public bool Register(string hotkeyText, out string error)
    {
        error = "";
        Unregister();
        if (!TryParse(hotkeyText, out var mods, out var vk))
        {
            error = "格式无法识别（示例：Alt+Q、Ctrl+Alt+Space）";
            return false;
        }

        EnsureMessageWindow();
        _registered = RegisterHotKey(_source!.Handle, HOTKEY_ID, mods | MOD_NOREPEAT, vk);
        if (!_registered)
        {
            var err = Marshal.GetLastWin32Error();
            error = err == 1409 ? "热键已被其他程序占用" : $"注册失败（Win32 错误 {err}）";
        }
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source != null)
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
        _registered = false;
    }

    private void EnsureMessageWindow()
    {
        if (_source != null) return;

        var parameters = new HwndSourceParameters("QuickPanel_HotkeyHost")
        {
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ParentWindow = new IntPtr(-3),            // HWND_MESSAGE（message-only 窗口）
        };
        _source = new HwndSource(parameters);

        if (!_hookAttached)
        {
            _source.AddHook(WndProc);
            _hookAttached = true;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            HotkeyPressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    /// <summary>解析 "Alt+Q"/"Ctrl+Alt+Space" 格式（PRD F30）。</summary>
    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win" or "windows": mods |= MOD_WIN; break;
                default: return false;
            }
        }

        var key = parts[^1].ToLowerInvariant();
        vk = key switch
        {
            var k when k.Length == 1 && char.IsAsciiLetter(k[0]) => (uint)char.ToUpperInvariant(k[0]),
            var k when k.Length == 1 && char.IsAsciiDigit(k[0]) => (uint)k[0],
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "`" or "~" => 0xC0,
            "-" or "_" => 0xBD,
            "=" or "+" => 0xBB,
            "[" or "{" => 0xDB,
            "]" or "}" => 0xDD,
            "\\" or "|" => 0xDC,
            ";" or ":" => 0xBA,
            "'" or "\"" => 0xDE,
            "," or "<" => 0xBC,
            "." or ">" => 0xBE,
            "/" or "?" => 0xBF,
            var k when k[0] == 'f' && k.Length is 2 or 3
                       && int.TryParse(k[1..], out var f) && f is >= 1 and <= 24
                => (uint)(0x70 + f - 1),
            _ => 0,
        };
        return vk != 0 && mods != 0;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }
}
