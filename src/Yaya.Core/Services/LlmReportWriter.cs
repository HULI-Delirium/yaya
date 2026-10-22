using System.Net.Http;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Yaya.Core.Services;

/// <summary>
/// LLM 复盘话术写出器（OpenAI 兼容 /chat/completions）。
/// 红线：请求体只允许 7 个聚合数值字段（窗口标题、进程名、时间轴一律不出机）；
/// 单发不重试、默认超时 5 秒；任何失败/校验不过 → 静默回退 RuleReportWriter 并留 Warning 日志。
/// </summary>
public sealed class LlmReportWriter : IReportWriter
{
    private const string SystemPrompt =
        "你是复盘话术润色器。只能基于给定数字，不得生成或修改任何数字与建议。禁止诊断、禁止评价成绩、禁止制造焦虑、禁止负面标签。输出恰好三句话，每句不超过 40 字。";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly LlmSettings _settings;
    private readonly HttpClient _client;

    /// <param name="handler">调用方注入；测试传 fake，不触真网络。</param>
    /// <param name="timeout">缺省 5 秒；测试可缩短避免真实等待。</param>
    public LlmReportWriter(LlmSettings settings, HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _settings = settings;
        _client = new HttpClient(handler) { Timeout = timeout ?? DefaultTimeout };
    }

    public string Write(DailyReportData data)
    {
        try
        {
            var content = RequestAsync(data).GetAwaiter().GetResult();
            if (OutputGuard.IsAcceptable(content, _settings.ForbiddenWords))
            {
                return content;
            }

            Log.Warning("LLM 复盘文案未通过输出校验，已回退规则模板版");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // 红线：日志绝不包含 api_key（此处只记异常，不 dump 请求）
            Log.Warning(ex, "LLM 复盘请求失败，已回退规则模板版");
        }

        return new RuleReportWriter().Write(data);
    }

    private async Task<string> RequestAsync(DailyReportData data)
    {
        var payload = new
        {
            model = _settings.Model,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserContent(data) },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _settings.BaseUrl.TrimEnd('/') + "/chat/completions");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await _client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new JsonException("LLM 响应缺少 choices[0].message.content");
    }

    // 红线：请求体只含这 7 个聚合数值字段，绝不附带标题/进程名/明细/时间线
    private static string BuildUserContent(DailyReportData data) =>
        JsonSerializer.Serialize(new
        {
            data.Date,
            data.FocusedMinutes,
            PeakWindowStart = data.PeakWindowStart?.ToString("hh\\:mm"),
            PeakWindowEnd = data.PeakWindowEnd?.ToString("hh\\:mm"),
            AdviceStartTime = data.AdviceStartTime?.ToString("HH:mm"),
            data.AdviceMinutes,
            data.DistractedMinutes,
            data.CorrectionCount,
        }, JsonOptions);
}
