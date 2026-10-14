namespace Yaya.Core.Monitoring;

/// <summary>状态机一次求值的结果：迁移信息 + 本轮是否触发轻干预询问。</summary>
public sealed record FocusTransition(FocusState From, FocusState To, bool ShouldNudge, NudgeKind? NudgeKind);
