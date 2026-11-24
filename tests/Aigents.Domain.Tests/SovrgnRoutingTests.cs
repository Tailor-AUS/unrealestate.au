using System.Net;
using System.Text;
using System.Text.Json;
using Aigents.Infrastructure.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aigents.Domain.Tests;

public class SovrgnRoutingTests
{
    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : "");
            return await handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    private static HttpResponseMessage OpenAiChatResponse(string content, string model = "gpt-5-mini", int totalTokens = 142)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-aigents-test",
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content },
                    finish_reason = "stop",
                },
            },
            usage = new
            {
                prompt_tokens = 90,
                completion_tokens = totalTokens - 90,
                total_tokens = totalTokens,
            },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
    }

    [Theory]
    [InlineData(null, "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("", "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("https://api.sovrgn.ai", "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("https://api.sovrgn.ai/", "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("https://api.sovrgn.ai/v1", "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("https://api.sovrgn.ai/v1/", "https://api.sovrgn.ai/v1/chat/completions")]
    [InlineData("https://api.sovrgn.ai/v1/chat/completions", "https://api.sovrgn.ai/v1/chat/completions")]
    public void BuildChatCompletionsUri_NormalizesHttpsUrls(string? raw, string expected)
    {
        var uri = SovrgnRoutingClient.BuildChatCompletionsUri(raw);
        Assert.NotNull(uri);
        Assert.Equal(expected, uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://api.sovrgn.ai/v1")]
    [InlineData("https://user:pass@api.sovrgn.ai/v1")]
    [InlineData("https://api.sovrgn.ai/v1?api-version=2024")]
    [InlineData("https://api.sovrgn.ai/v1#section")]
    [InlineData("not-a-uri")]
    public void BuildChatCompletionsUri_RejectsUnsafeUrlsFailClosed(string raw)
    {
        Assert.Null(SovrgnRoutingClient.BuildChatCompletionsUri(raw));
    }

    [Fact]
    public void BuildPayload_GatesTemperatureByModelCapability()
    {
        var reasoningPayload = SovrgnRoutingClient.BuildPayload(
            "tailor-fast",
            [("system", "sys"), ("user", "hi")],
            1200,
            0.7,
            jsonObjectResponse: false);
        using var reasoningDoc = JsonDocument.Parse(reasoningPayload);
        Assert.False(reasoningDoc.RootElement.TryGetProperty("temperature", out _));
        Assert.False(reasoningDoc.RootElement.TryGetProperty("response_format", out _));

        var legacyPayload = SovrgnRoutingClient.BuildPayload(
            "gpt-4.1-mini",
            [("system", "sys"), ("user", "hi")],
            1200,
            0.3,
            jsonObjectResponse: true);
        using var legacyDoc = JsonDocument.Parse(legacyPayload);
        Assert.True(legacyDoc.RootElement.TryGetProperty("temperature", out var temp));
        Assert.Equal(0.3, temp.GetDouble(), precision: 2);
        Assert.Equal("json_object", legacyDoc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task AzureAiService_ChatAsync_RoutesThroughSovrgn_WhenSovrgnApiKeySet()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenAiChatResponse("Paddington median house price is strong.", "gpt-5-mini", 210)));

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SOVRGN_API_KEY"] = "sovrgn_test_aigents_key",
            ["SOVRGN_API_URL"] = "https://api.sovrgn.ai/v1",
        });

        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions()),
            Options.Create(new AzureAiOptions { Endpoint = "https://legacy.openai.azure.com/", ApiKey = "legacy-key" }),
            config,
            NullLogger<SovrgnRoutingClient>.Instance);

        var service = new AzureAiService(
            Options.Create(new AzureAiOptions { Endpoint = "https://legacy.openai.azure.com/", ApiKey = "legacy-key" }),
            sovrgn,
            NullLogger<AzureAiService>.Instance);

        var res = await service.ChatAsync(
            [new ChatMessage { Role = "user", Content = "Tell me about Paddington QLD." }],
            "buy");

        Assert.Equal("Paddington median house price is strong.", res.Content);
        Assert.Equal(210, res.TokensUsed);
        Assert.Equal("gpt-5-mini", res.Model);

        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("https://api.sovrgn.ai/v1/chat/completions", req.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
        Assert.Equal("sovrgn_test_aigents_key", req.Headers.Authorization?.Parameter);
        Assert.Equal("aigents", req.Headers.GetValues("X-Sovrgn-Tenant").Single());

        using var bodyDoc = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("tailor-fast", bodyDoc.RootElement.GetProperty("model").GetString());
        Assert.False(bodyDoc.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task AzureAiService_SearchPropertyIntelligenceAsync_RequestsJsonObjectAndParsesResult()
    {
        var propertyJson = """
            {
              "bedrooms": 4,
              "bathrooms": 2,
              "carSpaces": 2,
              "landSizeM2": 607,
              "propertyType": "house",
              "estimatedValueLow": 1450000,
              "estimatedValueHigh": 1650000,
              "suburb": "Paddington",
              "state": "QLD",
              "postcode": "4064",
              "confidence": "high",
              "sources": ["Domain.com.au"]
            }
            """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenAiChatResponse(propertyJson)));

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SOVRGN_API_KEY"] = "sovrgn_test_aigents_key",
        });

        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions()),
            Options.Create(new AzureAiOptions()),
            config,
            NullLogger<SovrgnRoutingClient>.Instance);

        var service = new AzureAiService(
            Options.Create(new AzureAiOptions()),
            sovrgn,
            NullLogger<AzureAiService>.Instance);

        var intel = await service.SearchPropertyIntelligenceAsync("12 Given Tce, Paddington QLD 4064");

        Assert.Equal(4, intel.Bedrooms);
        Assert.Equal("Paddington", intel.Suburb);
        Assert.Equal("high", intel.Confidence);

        using var bodyDoc = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("json_object", bodyDoc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(bodyDoc.RootElement.GetProperty("max_tokens").GetInt32() >= 1000);
    }

    [Fact]
    public async Task AzureAiService_SupportsZeroMountCutover_WhenAzureEndpointPointsToSovrgn()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenAiChatResponse("Zero-mount cutover succeeded.", "gpt-4.1", 120)));

        var azureOpts = new AzureAiOptions
        {
            Endpoint = "https://api.sovrgn.ai/v1",
            ApiKey = "sovrgn_via_azure_secret_file",
            DeploymentName = "gpt-4.1",
        };

        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions()),
            Options.Create(azureOpts),
            BuildConfig(),
            NullLogger<SovrgnRoutingClient>.Instance);

        var service = new AzureAiService(
            Options.Create(azureOpts),
            sovrgn,
            NullLogger<AzureAiService>.Instance);

        var res = await service.ChatAsync(
            [new ChatMessage { Role = "user", Content = "Hello" }],
            "sell");

        Assert.Equal("Zero-mount cutover succeeded.", res.Content);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.sovrgn.ai/v1/chat/completions", handler.Requests[0].RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("sovrgn_via_azure_secret_file", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal("aigents", handler.Requests[0].Headers.GetValues("X-Sovrgn-Tenant").Single());
    }

    [Fact]
    public async Task AzureAiService_FailsClosed_WhenSovrgnKeySetWithInvalidUrl()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Must never make an outbound call on invalid SOVRGN_API_URL"));

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SOVRGN_API_KEY"] = "sovrgn_test_aigents_key",
            ["SOVRGN_API_URL"] = "http://insecure.example/v1",
        });

        var azureOpts = new AzureAiOptions
        {
            Endpoint = "https://legacy.openai.azure.com/",
            ApiKey = "legacy-key",
        };

        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions()),
            Options.Create(azureOpts),
            config,
            NullLogger<SovrgnRoutingClient>.Instance);

        var service = new AzureAiService(
            Options.Create(azureOpts),
            sovrgn,
            NullLogger<AzureAiService>.Instance);

        // Must fail closed instead of silently bypassing to direct Azure OpenAI
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync([new ChatMessage { Role = "user", Content = "Test" }], "buy"));
        Assert.Empty(handler.Requests);
    }
}
