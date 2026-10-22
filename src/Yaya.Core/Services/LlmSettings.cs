namespace Yaya.Core.Services;

/// <summary>
/// LLM 话术设置。红线：api_key 只存本地 settings.json，任何日志不得打印；
/// 总开关关闭、key 为空或 base_url 非 https 时一律视为关闭（零网络）。
/// </summary>
public sealed record LlmSettings
{
    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>禁语表；null 时使用 <see cref="OutputGuard.DefaultForbiddenWords"/>。</summary>
    public IReadOnlyList<string>? ForbiddenWords { get; init; }

    /// <summary>是否实际启用：总开关 ∧ key 非空 ∧ base_url 为 https。</summary>
    public bool IsEffectivelyEnabled =>
        Enabled
        && !string.IsNullOrWhiteSpace(ApiKey)
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
