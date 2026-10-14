namespace Yaya.Core.Monitoring;

/// <summary>用户纠正的类型：决定状态机的后续动作与 corrections 表的落库内容。</summary>
public enum UserCorrectionKind
{
    /// <summary>“刚才算有效”：回退到 Focused，并结束微重启窗口。</summary>
    CountAsValid = 0,

    /// <summary>“逃避”：进入微重启窗口（默认 2 分钟），窗口内不询问。</summary>
    Escape = 1,
}
