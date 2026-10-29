using System.Text.RegularExpressions;

namespace Yaya.Core.Monitoring;

/// <summary>
/// 浏览器标题清洗（纯函数）：去掉「 - Google Chrome」「 - Profile 1 - Microsoft Edge」
/// 「 — Mozilla Firefox」等宿主后缀，只留下页面标题用于站点词匹配。
/// 只处理已知宿主后缀；未识别浏览器保持原样，交给站点词/用户规则。
/// </summary>
public static class BrowserTitleCleaner
{
    // 宿主后缀：Chrome / Edge / Firefox / Brave / Opera / Vivaldi / 国产浏览器；允许尾部 (Beta) 等
    private static readonly Regex HostSuffix = new(
        @"\s*[-–—]\s*(?:Google\s+Chrome|Microsoft\s+Edge|Mozilla\s+Firefox|Brave|Opera|Vivaldi|QQ浏览器|360安全浏览器|360极速浏览器|搜狗浏览器|Maxthon)(?:\s*\([^)]*\))?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Edge 的 Profile 段：Profile 1 / 个人 / 工作 / Personal / Work
    private static readonly Regex ProfileSuffix = new(
        @"\s*[-–—]\s*(?:Profile\s*\d+|个人|工作|Personal|Work)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>清洗浏览器标题；空标题返回空串。非浏览器调用方不应使用本方法。</summary>
    public static string Clean(string? windowTitle)
    {
        var text = RuleText.Normalize(windowTitle);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // 标题里偶发的零宽字符不参与匹配
        text = text.Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\uFEFF", string.Empty);

        text = HostSuffix.Replace(text, string.Empty);
        text = ProfileSuffix.Replace(text, string.Empty);
        return text.Trim();
    }
}
