namespace Yaya.Core.Services;

/// <summary>
/// 复盘文案写出器。本周实现为规则模板版（<see cref="RuleReportWriter"/>）；
/// 第 3 周 LLM 版只需实现本接口，聚合（DailyReportAggregator）与 UI 均不感知。
/// </summary>
public interface IReportWriter
{
    /// <summary>把聚合结果写成"三句话"展示文本（产品红线：硬约束恰好三句，由测试数「。」）。</summary>
    string Write(DailyReportData data);
}
