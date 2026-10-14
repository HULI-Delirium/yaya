namespace Yaya.Core.Monitoring;

/// <summary>
/// 一次用户纠正的记录（纯数据）：由状态机返回，调用方决定如何落库（corrections 表），
/// 状态机本身保持零 IO。
/// </summary>
public sealed record CorrectionRecord(
    DateTimeOffset AtUtc,
    UserCorrectionKind Kind,
    FocusState OriginalState,
    FocusState ResultingState,
    string? Note);
