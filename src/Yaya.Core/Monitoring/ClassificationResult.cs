namespace Yaya.Core.Monitoring;

/// <summary>分类来源，用于 UI 解释「凭什么」与日志结构化字段。</summary>
public enum ClassificationSource
{
    Unknown = 0,
    UserProcess = 1,
    UserTitle = 2,
    BuiltinProcess = 3,
    BuiltinSite = 4,
    BrowserFallback = 5,
}

/// <summary>
/// 一次分类的完整结果：类别 + 来源 + 命中规则/站点 + 原因。
/// Reason 面向用户，可能包含规则值/进程名，**不写日志**；日志只记结构化字段（见 ARCHITECTURE §8）。
/// </summary>
public sealed record ClassificationResult(
    AppCategory Category,
    ClassificationSource Source,
    string? RuleId,
    string? MatchedSite,
    bool IsMuted,
    string Reason)
{
    public static ClassificationResult Unknown(string reason) =>
        new(AppCategory.Unknown, ClassificationSource.Unknown, null, null, false, reason);
}
