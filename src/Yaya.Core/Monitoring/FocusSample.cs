namespace Yaya.Core.Monitoring;

/// <summary>一次行为观测样本：类别 + 键鼠空闲时长 + 观测时刻。</summary>
public readonly record struct FocusSample(AppCategory Category, double IdleSeconds, DateTimeOffset AtUtc);
