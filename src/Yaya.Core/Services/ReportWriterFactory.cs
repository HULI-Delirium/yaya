using System.Net.Http;

namespace Yaya.Core.Services;

/// <summary>
/// 复盘写出器组合根：按 LlmSettings 选择实现。
/// 关闭/不可用 → 直接返回 RuleReportWriter（该代码路径零网络，测试断言 handler 不被调用）。
/// </summary>
public static class ReportWriterFactory
{
    public static IReportWriter Create(LlmSettings settings, HttpMessageHandler? handler = null) =>
        settings.IsEffectivelyEnabled
            ? new LlmReportWriter(settings, handler ?? new HttpClientHandler())
            : new RuleReportWriter();
}
