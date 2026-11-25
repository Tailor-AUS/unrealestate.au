using System.Net;
using System.Text;
using System.Text.Json;
using Aigents.Domain.Enquiries;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Services.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aigents.Domain.Tests;

public class SovrgnVoiceAndEnquiryTests
{
    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : "");
            return await handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static ListingVoiceAssistantRequest SampleRequest() => new(
        ListingId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Address: "42 Latrobe Terrace",
        Suburb: "Paddington",
        State: "QLD",
        Postcode: "4064",
        PropertyType: "House",
        PriceDisplay: "Offers over $1.65M",
        Bedrooms: 4,
        Bathrooms: 3,
        CarSpaces: 2,
        LandSize: 486,
        Description: "Renovated Queenslander with pool.",
        AgentName: "Sarah Lawson",
        AgentPhone: "0412 345 678");

    [Theory]
    [InlineData("https://api.sovrgn.ai/v1", "agents/create", "https://api.sovrgn.ai/v1/convai/agents/create")]
    [InlineData("https://api.sovrgn.ai/v1/chat/completions", "agents/create", "https://api.sovrgn.ai/v1/convai/agents/create")]
    [InlineData("https://api.sovrgn.ai", "conversation/get-signed-url?agent_id=abc", "https://api.sovrgn.ai/v1/convai/conversation/get-signed-url?agent_id=abc")]
    public void BuildConvaiUri_TargetsSovrgnConvaiProxy(string baseUrl, string rel, string expected)
    {
        var uri = SovrgnVoiceAssistantClient.BuildConvaiUri(baseUrl, rel);
        Assert.NotNull(uri);
        Assert.Equal(expected, uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://api.sovrgn.ai/v1", "agents/create")]
    [InlineData("https://api.sovrgn.ai/v1", "../secret")]
    [InlineData("https://api.sovrgn.ai/v1", "%2e%2e/secret")]
    [InlineData("https://api.sovrgn.ai/v1", "foo\\bar")]
    public void BuildConvaiUri_RejectsInsecureOrTraversalPaths(string baseUrl, string rel)
    {
        Assert.Null(SovrgnVoiceAssistantClient.BuildConvaiUri(baseUrl, rel));
    }

    [Fact]
    public async Task ProvisionListingAssistantAsync_RoutesThroughSovrgnConvaiAndReturnsAgentId()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"agent_id":"agent_el_12345"}""", Encoding.UTF8, "application/json")
            }));

        var factory = new StubHttpClientFactory(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SOVRGN_API_KEY"] = "sovrgn_test_key"
        }).Build();

        var routing = new SovrgnRoutingClient(
            factory,
            Options.Create(new SovrgnAiOptions()),
            Options.Create(new AzureAiOptions()),
            config,
            NullLogger<SovrgnRoutingClient>.Instance);

        var voice = new SovrgnVoiceAssistantClient(routing, factory, NullLogger<SovrgnVoiceAssistantClient>.Instance);

        var result = await voice.ProvisionListingAssistantAsync(SampleRequest());

        Assert.True(result.Succeeded);
        Assert.Equal("agent_el_12345", result.AgentId);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.sovrgn.ai/v1/convai/agents/create", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("sovrgn_test_key", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal("aigents", handler.Requests[0].Headers.GetValues("X-Sovrgn-Tenant").Single());

        using var bodyDoc = JsonDocument.Parse(handler.Bodies[0]);
        var prompt = bodyDoc.RootElement
            .GetProperty("conversation_config")
            .GetProperty("agent")
            .GetProperty("prompt")
            .GetProperty("prompt")
            .GetString();
        Assert.Contains("42 Latrobe Terrace", prompt);
        Assert.Contains("Sarah Lawson", prompt);
        Assert.Contains("never guess or invent", prompt);
    }

    [Fact]
    public async Task ProvisionAndSignedUrl_HandleMalformedJsonAndUnsafeSignedUrlGracefully()
    {
        var badJsonHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html")
            }));

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SOVRGN_API_KEY"] = "sovrgn_test_key"
        }).Build();

        var f1 = new StubHttpClientFactory(badJsonHandler);
        var r1 = new SovrgnRoutingClient(f1, Options.Create(new SovrgnAiOptions()), Options.Create(new AzureAiOptions()), cfg, NullLogger<SovrgnRoutingClient>.Instance);
        var v1 = new SovrgnVoiceAssistantClient(r1, f1, NullLogger<SovrgnVoiceAssistantClient>.Instance);

        var prov = await v1.ProvisionListingAssistantAsync(SampleRequest());
        Assert.False(prov.Succeeded);
        Assert.Equal("invalid_upstream_json", prov.ErrorCode);

        var badSchemeHandler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"signed_url":"javascript:alert(1)"}""", Encoding.UTF8, "application/json")
            }));
        var f2 = new StubHttpClientFactory(badSchemeHandler);
        var r2 = new SovrgnRoutingClient(f2, Options.Create(new SovrgnAiOptions()), Options.Create(new AzureAiOptions()), cfg, NullLogger<SovrgnRoutingClient>.Instance);
        var v2 = new SovrgnVoiceAssistantClient(r2, f2, NullLogger<SovrgnVoiceAssistantClient>.Instance);

        var session = await v2.GetSignedConversationUrlAsync("agent_el_12345");
        Assert.False(session.Succeeded);
        Assert.Equal("invalid_signed_url", session.ErrorCode);
    }

    [Fact]
    public async Task ProvisionListingAssistantAsync_WhenUnconfigured_FailsClosedWithPromptPreview()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Must never make outbound call when unconfigured"));

        var factory = new StubHttpClientFactory(handler);
        var routing = new SovrgnRoutingClient(
            factory,
            Options.Create(new SovrgnAiOptions()),
            Options.Create(new AzureAiOptions()),
            new ConfigurationBuilder().Build(),
            NullLogger<SovrgnRoutingClient>.Instance);

        var voice = new SovrgnVoiceAssistantClient(routing, factory, NullLogger<SovrgnVoiceAssistantClient>.Instance);
        Assert.False(voice.IsConfigured);

        var result = await voice.ProvisionListingAssistantAsync(SampleRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("sovrgn_not_configured", result.ErrorCode);
        Assert.Contains("42 Latrobe Terrace", result.SystemPromptPreview);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void EnquiryRecordCodec_HandlesAuLandlinesSpoofingAndInvalidPhones()
    {
        var encoded = EnquiryRecordCodec.Encode(
            EnquiryChannel.CallRequest,
            "Interested in inspection at 10:30am",
            "Alex · phone: 0499999999",
            buyerPhone: "(07) 3123 4567",
            buyerEmail: "alex@example.test");

        var parsed = EnquiryRecordCodec.Parse(new ListingInquiry
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            Message = encoded,
            Status = InquiryStatus.New,
            CreatedAt = DateTime.UtcNow
        }, "42 Latrobe Terrace", "Paddington");

        Assert.Equal(EnquiryChannel.CallRequest, parsed.Channel);
        Assert.Equal("07 3123 4567", parsed.BuyerPhone);
        Assert.Equal("tel:+61731234567", EnquiryRecordCodec.BuildTelHref(parsed.BuyerPhone));
        Assert.StartsWith("sms:+61731234567?body=", EnquiryRecordCodec.BuildSmsHref(parsed.BuyerPhone, "Hi Alex"));
        Assert.DoesNotContain("0499999999", parsed.BuyerPhone);

        // Malformed phone strings must be rejected
        Assert.Null(EnquiryRecordCodec.NormalizeDialablePhone("++++++++"));
        Assert.Null(EnquiryRecordCodec.NormalizeDialablePhone("0412+345"));
        Assert.Null(EnquiryRecordCodec.NormalizeDialablePhone("١٢٣٤٥٦٧٨٩٠"));
    }
}
