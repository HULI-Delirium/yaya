using Yaya.Core.Services;

namespace Yaya.Core.Tests;

public class LlmSettingsTests
{
    [Fact]
    public void IsEffectivelyEnabled_默认_关闭()
    {
        Assert.False(new LlmSettings().IsEffectivelyEnabled);
    }

    [Theory]
    // 开总开关但 key 为空 → 关闭
    [InlineData(true, "https://llm.example.com", "m", "", false)]
    // 开总开关但 base_url 为 http → 关闭
    [InlineData(true, "http://llm.example.com", "m", "key", false)]
    // 开总开关但 base_url 不是合法 URI → 关闭
    [InlineData(true, "not a url", "m", "key", false)]
    // 三者齐备 → 启用
    [InlineData(true, "https://llm.example.com/v1", "m", "key", true)]
    // 总开关关 → 其余齐备也不启用
    [InlineData(false, "https://llm.example.com", "m", "key", false)]
    public void IsEffectivelyEnabled_组合判定(bool enabled, string baseUrl, string model, string key, bool expected)
    {
        var settings = new LlmSettings { Enabled = enabled, BaseUrl = baseUrl, Model = model, ApiKey = key };
        Assert.Equal(expected, settings.IsEffectivelyEnabled);
    }
}
