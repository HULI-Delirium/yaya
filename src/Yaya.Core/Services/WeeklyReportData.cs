namespace Yaya.Core.Services;

/// <summary>周报聚合产物（纯本地规则版，永不联网、不接 LLM）。</summary>
public sealed record WeeklyReportData(
    DateOnly WeekStart,             // 周一（本地）
    int TotalFocusedMinutes,
    int FocusedDays,                // 专注日 X/7
    int PomodorosCompleted,         // events 中 PomodoroFocusCompleted 计数（Aborted 不计）
    TimeOnly? AdviceStartTime,      // 下周建议起点（复用“明天第一个动作”规则；无则 null）
    int AdviceMinutes);
