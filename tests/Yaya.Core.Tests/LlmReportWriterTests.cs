using System.Net;
using Yaya.Core.Services;

namespace Yaya.Core.Tests;

public class LlmReportWriterTests
{
    private static readonly DailyReportData Data = new(
        new DateOnly(2026, 10, 22), 87,
        TimeSpan.FromHours(15) + TimeSpan.FromMinutes(30), TimeSpan.FromHours(16),
        new TimeOnly(15, 30), 25, 40, 2);

    private static readonly LlmSettings EnabledSettings = new()
    {
        Enabled = true,
        BaseUrl = "https://llm.example.com/v1",
        Model = "test-model",
        ApiKey = "test-key",
    };

    private const string ValidContent = "今天有效专注 87 分钟。状态在线，值得复制。明天 15:30 先做最难的一题。";

    /// <summary>计数 fake：可预设响应、可模拟超时/异常；记录调用次数（零调用断言用）。</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Responder?.Invoke(request, cancellationToken)
                ?? Task.FromResult(Ok(ValidContent));
        }

        public static HttpResponseMessage Ok(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":""" + JsonQuote(content) + """}}]}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };

        private static string JsonQuote(string s) =>
            System.Text.Json.JsonSerializer.Serialize(s);
    }

    private static LlmReportWriter CreateWriter(FakeHandler handler, TimeSpan? timeout = null) =>
        new(EnabledSettings, handler, timeout);

    [Fact]
    public void Write_LLM返回合规文案_原文采用()
    {
        var handler = new FakeHandler();
        var writer = CreateWriter(handler);

        var text = writer.Write(Data);

        Assert.Equal(ValidContent, text);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Write_LLM请求超时_回退规则模板版()
    {
        var handler = new FakeHandler
        {
            Responder = (_, ct) => Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("模拟 HttpClient 超时")),
        };
        var writer = CreateWriter(handler, TimeSpan.FromMilliseconds(100));

        var text = writer.Write(Data);

        Assert.Equal(new RuleReportWriter().Write(Data), text);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Write_LLM返回500_回退规则模板版()
    {
        var handler = new FakeHandler
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        };
        var writer = CreateWriter(handler);

        var text = writer.Write(Data);

        Assert.Equal(new RuleReportWriter().Write(Data), text);
    }

    [Fact]
    public void Write_LLM返回非三句_回退规则模板版()
    {
        var handler = new FakeHandler { Responder = (_, _) => Task.FromResult(FakeHandler.Ok("只有一句话。")) };
        var writer = CreateWriter(handler);

        Assert.Equal(new RuleReportWriter().Write(Data), writer.Write(Data));
    }

    [Fact]
    public void Write_LLM文案命中禁语_回退规则模板版()
    {
        var handler = new FakeHandler
        {
            Responder = (_, _) => Task.FromResult(FakeHandler.Ok("今天有点焦虑很正常。明天继续。加油干。")),
        };
        var writer = CreateWriter(handler);

        Assert.Equal(new RuleReportWriter().Write(Data), writer.Write(Data));
    }

    [Theory]
    [InlineData("今天专注 87 分钟！状态不错。明天继续哦。")]
    [InlineData("今天专注 87 分钟？明天继续。加油干哦。")]
    [InlineData("今天专注 87 分钟! 明天继续。加油干哦。")]
    [InlineData("今天专注 87 分钟? 明天继续。加油干哦。")]
    public void Write_LLM文案含感叹号或问号_回退规则模板版(string content)
    {
        var handler = new FakeHandler { Responder = (_, _) => Task.FromResult(FakeHandler.Ok(content)) };
        var writer = CreateWriter(handler);

        Assert.Equal(new RuleReportWriter().Write(Data), writer.Write(Data));
    }

    [Theory]
    [InlineData("一句。二句。三句。", true)]
    [InlineData("只有两句。到这里。", false)]
    [InlineData("一。二。三。四。", false)]
    public void Guard_句号计数口径_恰好三句才通过(string text, bool expected) =>
        Assert.Equal(expected, OutputGuard.IsAcceptable(text));

    [Fact]
    public void Guard_总长超过120字_拒绝()
    {
        var text = new string('字', 118) + "。二。三。";   // 121 字、恰好三句
        Assert.False(OutputGuard.IsAcceptable(text));
    }

    [Fact]
    public void Factory_LLM关闭_返回规则版且Handler零调用()
    {
        var handler = new FakeHandler();

        var writer = ReportWriterFactory.Create(new LlmSettings(), handler);

        Assert.IsType<RuleReportWriter>(writer);
        Assert.Equal(0, handler.CallCount);
    }
}
