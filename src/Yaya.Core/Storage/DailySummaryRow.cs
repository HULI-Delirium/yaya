namespace Yaya.Core.Storage;

/// <summary>daily_summary 表完整行（导出用）。</summary>
public sealed record DailySummaryRow(
    string Date,
    int FocusedMinutes,
    int DistractedMinutes,
    int CorrectionCount,
    string SummaryText,
    DateTimeOffset CreatedAtUtc,
    string? ReportJson);
