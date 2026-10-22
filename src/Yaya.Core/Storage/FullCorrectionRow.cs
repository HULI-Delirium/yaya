namespace Yaya.Core.Storage;

/// <summary>corrections 表完整行（导出用）。</summary>
public sealed record FullCorrectionRow(
    DateTimeOffset AtUtc,
    string OriginalState,
    string CorrectedState,
    string Kind,
    string AppCategory,
    string? Note);
