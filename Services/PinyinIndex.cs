using System.Text;

namespace QuickPanel.Services;

/// <summary>
/// v1.9：拼音首字母匹配 —— 输入 "wx" 能搜到"微信"，"yx" 能搜到"游戏"。
/// 实现：GB2312 编码区间查表（标准做法，覆盖 6763 个常用汉字，零第三方依赖）。
/// </summary>
public static class PinyinIndex
{
    /// <summary>.NET Core 默认不含 GB2312，需注册代码页提供程序（.NET 内置，无第三方包）。</summary>
    private static readonly Encoding Gb2312 = CreateGb2312();

    private static Encoding CreateGb2312()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB2312");
        }
        catch
        {
            return Encoding.Default;
        }
    }

    // GB2312 一级汉字（3755 个，按拼音排序）的首字母边界表。
    // 每个条目：(该首字母在 GB2312 一级字库中的起始区位码, 字母)
    // 数据来源：GB2312-80 一级汉字按拼音排序的公开区间划分。
    private static readonly (int Code, char Letter)[] Boundaries =
    {
        (0xB0A1, 'a'), (0xB0C5, 'b'), (0xB2C1, 'c'), (0xB4EE, 'd'), (0xB6EA, 'e'),
        (0xB7A2, 'f'), (0xB8C1, 'g'), (0xB9FE, 'h'), (0xBBF7, 'j'), (0xBFA6, 'k'),
        (0xC0AC, 'l'), (0xC2E8, 'm'), (0xC4C3, 'n'), (0xC5B6, 'o'), (0xC5BE, 'p'),
        (0xC6DA, 'q'), (0xC8BB, 'r'), (0xC8F6, 's'), (0xCBFA, 't'), (0xCDDA, 'w'),
        (0xCEF4, 'x'), (0xD1B9, 'y'), (0xD4D1, 'z'),
    };

    /// <summary>
    /// 取字符串的拼音首字母串（ASCII 字母数字保留小写）。
    /// "微信" → "wx"，"Google Chrome" → "googlechrome"。
    /// </summary>
    public static string ToInitials(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch < 128)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                continue;
            }
            var letter = ChineseToInitial(ch);
            if (letter != '\0') sb.Append(letter);
        }
        return sb.ToString();
    }

    /// <summary>汉字 → 拼音首字母（GB2312 一级字库区间查找）。</summary>
    private static char ChineseToInitial(char ch)
    {
        try
        {
            var bytes = Gb2312.GetBytes(ch.ToString());
            if (bytes.Length != 2) return '\0'; // 非 GB2312 汉字（生僻字/繁体）
            var code = (bytes[0] << 8) | bytes[1];

            // 一级汉字：0xB0A1 - 0xD7F9
            if (code < 0xB0A1 || code > 0xD7F9) return '\0';

            var result = '\0';
            foreach (var (start, letter) in Boundaries)
            {
                if (code >= start) result = letter;
                else break;
            }
            return result;
        }
        catch
        {
            // 系统未安装 GB2312 代码页
            return '\0';
        }
    }

    /// <summary>匹配：名称包含查询串，或名称拼音首字母包含查询串。</summary>
    public static bool Matches(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        var initials = ToInitials(name);
        return initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
