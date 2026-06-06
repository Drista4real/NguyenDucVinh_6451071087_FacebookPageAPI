using System.Net;
using CoreService.Options;
using CoreService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreService.Tests;

public sealed class AiAnalysisServiceTests
{
    [Fact]
    public async Task FallbackAiAnalysisService_UsesRuleBased_WhenOpenAiFails()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        var options = Microsoft.Extensions.Options.Options.Create(new AiApiOptions
        {
            Provider = "OpenAI",
            OpenAi = new OpenAiOptions
            {
                ApiKey = "test-key",
                Model = "gpt-5.4-mini",
                BaseUrl = "https://api.openai.com/v1/"
            }
        });
        var openAi = new OpenAiAnalysisService(httpClient, options);
        var service = new FallbackAiAnalysisService(
            openAi,
            new RuleBasedAiAnalysisService(),
            options,
            NullLogger<FallbackAiAnalysisService>.Instance);

        var result = await service.AnalyzeCommentAsync("Shop oi gia bao nhieu?");

        Assert.Equal("ask_price", result.Intent);
        Assert.Equal("rule_based", result.Source);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticResponseHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{}")
            });
    }
}
