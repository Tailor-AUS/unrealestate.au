using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Aigents.Infrastructure.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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

    private sealed class FakeResilienceHandler : DelegatingHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FakeServiceDiscoveryHandler : DelegatingHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    private static HttpResponseMessage OpenAiChatResponse(
        string content,
        string model = "gpt-5-mini",
        int? totalTokens = 142,
        int promptTokens = 90,
        int completionTokens = 52,
        string finishReason = "stop")
    {
        object usage = totalTokens.HasValue
            ? new { prompt_tokens = promptTokens, completion_tokens = completionTokens, total_tokens = totalTokens.Value }
            : new { prompt_tokens = promptTokens, completion_tokens = completionTokens };

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
                    finish_reason = finishReason,
                },
            },
            usage,
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
    [InlineData("https://@api.sovrgn.ai/v1")]
    [InlineData("https://api.sovrgn.ai/v1?api-version=2024")]
    [InlineData("https://api.sovrgn.ai/v1?")]
    [InlineData("https://api.sovrgn.ai/v1#section")]
    [InlineData("https://api.sovrgn.ai/v1#")]
    [InlineData("not-a-uri")]
    public void BuildChatCompletionsUri_RejectsUnsafeUrlsFailClosed(string raw)
    {
        Assert.Null(SovrgnRoutingClient.BuildChatCompletionsUri(raw));
    }

    [Fact]
    public void IsAzureOpenAiEndpoint_RequiresCleanHttpsAzureHost()
    {
        Assert.True(SovrgnRoutingClient.IsAzureOpenAiEndpoint("https://aigents-ai.openai.azure.com/"));
        Assert.True(SovrgnRoutingClient.IsAzureOpenAiEndpoint("https://aigents-ai.cognitiveservices.azure.com/"));
        Assert.False(SovrgnRoutingClient.IsAzureOpenAiEndpoint("http://aigents-ai.openai.azure.com/"));
        Assert.False(SovrgnRoutingClient.IsAzureOpenAiEndpoint("https://user@aigents-ai.openai.azure.com/"));
        Assert.False(SovrgnRoutingClient.IsAzureOpenAiEndpoint("https://api.sovrgn.ai/v1"));
    }

    [Fact]
    public void BuildPayload_GatesTemperatureByModelCapability_AndClampsMaxTokens()
    {
        var reasoningPayload = SovrgnRoutingClient.BuildPayload(
            "tailor-fast",
            [("system", "sys"), ("user", "hi")],
            100, // clamped to 256
            0.7,
            jsonObjectResponse: false);
        using var reasoningDoc = JsonDocument.Parse(reasoningPayload);
        Assert.Equal(256, reasoningDoc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(reasoningDoc.RootElement.TryGetProperty("temperature", out _));
        Assert.False(reasoningDoc.RootElement.TryGetProperty("response_format", out _));

        var legacyPayload = SovrgnRoutingClient.BuildPayload(
            "gpt-4.1-mini",
            [("system", "sys"), ("user", "hi")],
            99999, // clamped to 4096
            0.3,
            jsonObjectResponse: true);
        using var legacyDoc = JsonDocument.Parse(legacyPayload);
        Assert.Equal(4096, legacyDoc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.True(legacyDoc.RootElement.TryGetProperty("temperature", out var temp));
        Assert.Equal(0.3, temp.GetDouble(), precision: 2);
        Assert.Equal("json_object", legacyDoc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public void AddSovrgnRouting_StripsGlobalResilienceAndServiceDiscovery_AndDisablesRedirects()
    {
        var services = new ServiceCollection();
        var config = BuildConfig();
        var fakeResilience = new FakeResilienceHandler();
        var fakeDiscovery = new FakeServiceDiscoveryHandler();

        // Simulate Aspire ServiceDefaults global ConfigureHttpClientDefaults
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddHttpMessageHandler(() => fakeResilience);
            http.AddHttpMessageHandler(() => fakeDiscovery);
        });

        services.AddSovrgnRouting(config);

        using var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        var factoryOpts = optionsMonitor.Get(SovrgnRoutingClient.HttpClientName);

        var builder = new TestHttpMessageHandlerBuilder(sp);
        foreach (var action in factoryOpts.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        Assert.IsType<SocketsHttpHandler>(builder.PrimaryHandler);
        Assert.False(((SocketsHttpHandler)builder.PrimaryHandler!).AllowAutoRedirect);
        Assert.DoesNotContain(builder.AdditionalHandlers, h => h is FakeResilienceHandler or FakeServiceDiscoveryHandler);

        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient(SovrgnRoutingClient.HttpClientName);
        Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
    }

    private sealed class TestHttpMessageHandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = SovrgnRoutingClient.HttpClientName;
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];
        public override IServiceProvider Services { get; } = services;
        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }

    [Fact]
    public async Task AzureAiService_ChatAsync_RoutesThroughSovrgn_SupportsAgentModeAndUsageFallback()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenAiChatResponse(
                "Paddington median house price is strong.",
                model: "gpt-5-mini",
                totalTokens: null,
                promptTokens: 120,
                completionTokens: 90)));

        // Bind via SovrgnAiOptions (Sovrgn:ApiKey) instead of flat env var
        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions { ApiKey = "sovrgn_options_key" }),
            Options.Create(new AzureAiOptions { Endpoint = "https://legacy.openai.azure.com/", ApiKey = "legacy-key" }),
            BuildConfig(),
            NullLogger<SovrgnRoutingClient>.Instance);

        var service = new AzureAiService(
            Options.Create(new AzureAiOptions { Endpoint = "https://legacy.openai.azure.com/", ApiKey = "legacy-key" }),
            sovrgn,
            NullLogger<AzureAiService>.Instance);

        // CallIntelligenceService passes mode="agent", which must fall back to buyer prompt
        var res = await service.ChatAsync(
            [new ChatMessage { Role = "user", Content = "Tell me about Paddington QLD." }],
            "agent");

        Assert.Equal("Paddington median house price is strong.", res.Content);
        Assert.Equal(210, res.TokensUsed); // prompt_tokens + completion_tokens fallback
        Assert.Equal("gpt-5-mini", res.Model);

        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("https://api.sovrgn.ai/v1/chat/completions", req.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
        Assert.Equal("sovrgn_options_key", req.Headers.Authorization?.Parameter);
        Assert.Equal("aigents", req.Headers.GetValues("X-Sovrgn-Tenant").Single());

        using var bodyDoc = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("tailor-fast", bodyDoc.RootElement.GetProperty("model").GetString());
        Assert.True(bodyDoc.RootElement.GetProperty("max_tokens").GetInt32() >= 2500);
        Assert.False(bodyDoc.RootElement.TryGetProperty("temperature", out _));
        Assert.Contains("buyer's agent", bodyDoc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
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
        Assert.True(bodyDoc.RootElement.GetProperty("max_tokens").GetInt32() >= 2000);
    }

    [Fact]
    public async Task AzureAiService_SupportsZeroMountCutover_AndHonorsSovrgnModelOverride()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenAiChatResponse("Zero-mount cutover succeeded.", "gpt-5-mini", 120)));

        var azureOpts = new AzureAiOptions
        {
            Endpoint = "https://api.sovrgn.ai/v1",
            ApiKey = "sovrgn_via_azure_secret_file",
            DeploymentName = "gpt-4.1",
        };

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SOVRGN_MODEL"] = "tailor-fast",
        });

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

        var res = await service.ChatAsync(
            [new ChatMessage { Role = "user", Content = "Hello" }],
            "sell");

        Assert.Equal("Zero-mount cutover succeeded.", res.Content);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.sovrgn.ai/v1/chat/completions", handler.Requests[0].RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("sovrgn_via_azure_secret_file", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal("aigents", handler.Requests[0].Headers.GetValues("X-Sovrgn-Tenant").Single());

        using var bodyDoc = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("tailor-fast", bodyDoc.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("https://api.sovrgn.ai/v1", null)]
    [InlineData("https://api.sovrgn.ai/v1", "")]
    [InlineData("http://insecure.example/v1", "some-key")]
    [InlineData("http://legacy.openai.azure.com/", "legacy-key")]
    public async Task AzureAiService_FailsClosed_OnNonAzureEndpointWithoutKeyOrInsecureUrl(
        string azureEndpoint,
        string? azureApiKey)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Must never make an outbound call on invalid/unauthenticated endpoint"));

        var azureOpts = new AzureAiOptions
        {
            Endpoint = azureEndpoint,
            ApiKey = azureApiKey,
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync([new ChatMessage { Role = "user", Content = "Test" }], "buy"));
        var intel = await service.SearchPropertyIntelligenceAsync("12 Given Tce, Paddington");
        Assert.Equal("error", intel.Confidence);
        Assert.Empty(handler.Requests);
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync([new ChatMessage { Role = "user", Content = "Test" }], "buy"));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task SovrgnRoutingClient_ThrowsHttpRequestException_WithoutLeakingApiKey(HttpStatusCode statusCode)
    {
        const string secretKey = "sovrgn_super_secret_do_not_leak";
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var resp = new HttpResponseMessage(statusCode);
            if (statusCode == HttpStatusCode.Redirect)
                resp.Headers.Location = new Uri("https://evil.example/steal");
            return Task.FromResult(resp);
        });

        var sovrgn = new SovrgnRoutingClient(
            new StubHttpClientFactory(handler),
            Options.Create(new SovrgnAiOptions { ApiKey = secretKey }),
            Options.Create(new AzureAiOptions()),
            BuildConfig(),
            NullLogger<SovrgnRoutingClient>.Instance);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sovrgn.CompleteChatAsync([("user", "hi")], 1024, null, false));
        Assert.Equal(statusCode, ex.StatusCode);
        Assert.DoesNotContain(secretKey, ex.Message);
    }
}
