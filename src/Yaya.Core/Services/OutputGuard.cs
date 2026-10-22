namespace Yaya.Core.Services;

/// <summary>
/// 禁语与合法性判定集中的输出校验（独立静态类便于单测）。
/// 口径（与设计稿一致，不得自行更改）：恰好三句（数「。」）、出现「！」「？」
/// （含半角 ! ?，从严）即拒、总长 ≤120 字、禁语表命中即拒。
/// </summary>
public static class OutputGuard
{
    /// <summary>默认禁语表；可经 LlmSettings.ForbiddenWords 覆盖。</summary>
    public static readonly string[] DefaultForbiddenWords =
    {
        "焦虑", "抑郁", "失败", "垃圾", "废", "差劲", "强迫症", "诊断", "病情",
    };

    public const int MaxTotalChars = 120;

    /// <summary>LLM 文案是否可采用；任一不通过即整体回退规则模板版。</summary>
    public static bool IsAcceptable(string text, IReadOnlyList<string>? forbiddenWords = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // 恰好三句（与“测试数句号”验收口径一致）
        if (text.Count(c => c == '。') != 3)
        {
            return false;
        }

        // 感叹号或反问从严：全角/半角一律拒
        if (text.Contains('！') || text.Contains('？') || text.Contains('!') || text.Contains('?'))
        {
            return false;
        }

        if (text.Length > MaxTotalChars)
        {
            return false;
        }

        var words = forbiddenWords ?? DefaultForbiddenWords;
        foreach (var word in words)
        {
            if (text.Contains(word, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
