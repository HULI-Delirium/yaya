namespace Yaya.Core.Services;

/// <summary>
/// 每日复盘文案生成（纯函数，可单测）。
/// 产品红线：复盘必须短——固定三句话，一句话一个信息点：
///   1. 今日学习/游离时长事实；
///   2. 一句针对性建议（按时长对比切换口径）；
///   3. 纠正记录回执（让用户知道纠正被听见了）。
/// </summary>
public static class DailySummaryComposer
{
    public static string Compose(int focusedMinutes, int distractedMinutes, int correctionCount)
    {
        var first = $"今天有效学习约 {focusedMinutes} 分钟，游离约 {distractedMinutes} 分钟。";

        var second = distractedMinutes > focusedMinutes
            ? "游离时间超过了学习时间，明天可以试试先收起跑偏的应用。"
            : "学习节律保持得不错，继续保持哦。";

        var third = correctionCount > 0
            ? $"今天你纠正了我 {correctionCount} 次，我会慢慢学懂你的习惯。"
            : "今天没有纠正记录，说明我的判断基本合你心意。";

        return first + second + third;
    }
}
