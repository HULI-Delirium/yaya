namespace Yaya.Core.Monitoring;

/// <summary>“轻干预询问”的类型：冷却按类型分别计时（同类询问 10 分钟内不重复）。</summary>
public enum NudgeKind
{
    /// <summary>游离提醒：询问用户是否回到学习。v1 仅此一种，枚举为后续扩展预留。</summary>
    ReturnToStudy = 0,
}
