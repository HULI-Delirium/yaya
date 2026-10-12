namespace Yaya.Core.Monitoring;

/// <summary>状态机一次求值的结果。</summary>
public sealed record FocusTransition(FocusState From, FocusState To, double Confidence, bool ShouldNotify);
