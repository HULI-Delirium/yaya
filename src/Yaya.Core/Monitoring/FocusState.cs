namespace Yaya.Core.Monitoring;

/// <summary>三态状态机：有效学习 / 游离 / 不确定。</summary>
public enum FocusState
{
    /// <summary>有效学习。</summary>
    Focused = 0,

    /// <summary>游离：娱乐、社交或长时间键鼠空闲。</summary>
    Distracted = 1,

    /// <summary>不确定：信号不足，只记录不打扰。</summary>
    Uncertain = 2,
}
