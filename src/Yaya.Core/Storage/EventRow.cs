namespace Yaya.Core.Storage;

/// <summary>events 表的一行（查询用纯数据）。State 为 FocusState 名、'AppExit' 或 'Pomodoro*'。</summary>
public sealed record EventRow(DateTimeOffset AtUtc, Monitoring.AppCategory Category, string State, double IdleSeconds);
