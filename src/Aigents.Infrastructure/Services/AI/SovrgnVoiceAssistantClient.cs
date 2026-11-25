using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.AI;

public sealed record ListingVoiceAssistantRequest(
    Guid ListingId,
    string Address,
    string Suburb,
    string State,
    string Postcode,
    string PropertyType,
    string PriceDisplay,
    int? Bedrooms,
    int? Bathrooms,
    int? CarSpaces,
    int? LandSize,
    string Description,
    string AgentName,
    string? AgentPhone,
    string? VoiceId = null);

public sealed record VoiceAssistantProvisionResult(
    bool Succeeded,
    string? AgentId,
    string? ProviderHost,
    string SystemPromptPreview,
    string? ErrorCode,
    string Message);

public sealed record VoiceConversationSessionResult(
    bool Succeeded,
    string? SignedUrl,
    string? ErrorCode,
    string Message);

public interface ISovrgnVoiceAssistantClient
{
    bool IsConfigured { get; }

    Task<VoiceAssistantProvisionResult> ProvisionListingAssistantAsync(
        ListingVoiceAssistantRequest request,
        CancellationToken ct = default);

    Task<VoiceConversationSessionResult> GetSignedConversationUrlAsync(
        string agentId,
        CancellationToken ct = default);
}

/// <summary>
/// Provisions and connects ElevenLabs Conversational AI property concierges strictly
/// through Tailor Routing (<c>https://api.sovrgn.ai/v1/convai/*</c>).
/// Never holds ElevenLabs vendor credentials directly — Sovrgn stamps <c>xi-api-key</c>
/// server-side using the existing <c>SOVRGN_API_KEY</c> + <c>X-Sovrgn-Tenant: aigents</c>.
/// Fails closed with honest status when unconfigured or when upstream rejects residency.
/// </summary>
public sealed class SovrgnVoiceAssistantClient : ISovrgnVoiceAssistantClient
{
    private readonly SovrgnRoutingClient _routingClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SovrgnVoiceAssistantClient> _logger;

    public SovrgnVoiceAssistantClient(
        SovrgnRoutingClient routingClient,
        IHttpClientFactory httpClientFactory,
        ILogger<SovrgnVoiceAssistantClient> logger)
    {
        _routingClient = routingClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool IsConfigured => _routingClient.TryResolveConfig(out var cfg, out _) && cfg is not null;

    public static Uri? BuildConvaiUri(string? rawBaseUrl, string relativePath)
    {
        var chatUri = SovrgnRoutingClient.BuildChatCompletionsUri(rawBaseUrl);
        if (chatUri is null) return null;

        var cleanRel = (relativePath ?? string.Empty).Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(cleanRel)
            || cleanRel.Contains("..", StringComparison.Ordinal)
            || cleanRel.Contains('\\')
            || cleanRel.Contains("%2e", StringComparison.OrdinalIgnoreCase)
            || cleanRel.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || cleanRel.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            || cleanRel.Contains('@'))
        {
            return null;
        }

        var left = chatUri.AbsoluteUri;
        const string suffix = "/chat/completions";
        var v1Root = left.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? left[..^suffix.Length]
            : left.TrimEnd('/');

        if (!Uri.TryCreate($"{v1Root}/convai/{cleanRel}", UriKind.Absolute, out var built))
            return null;

        // Assert host, port, and HTTPS scheme did not shift
        if (built.Scheme != Uri.UriSchemeHttps
            || !string.Equals(built.Host, chatUri.Host, StringComparison.OrdinalIgnoreCase)
            || built.Port != chatUri.Port
            || !string.IsNullOrEmpty(built.UserInfo))
        {
            return null;
        }

        return built;
    }

    public static bool IsValidSignedVoiceUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "wss" && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host)) return false;
        return true;
    }

    public static string BuildListingSystemPrompt(ListingVoiceAssistantRequest r)
    {
        var specs = new List<string>();
        if (r.Bedrooms.HasValue) specs.Add($"{r.Bedrooms} bedrooms");
        if (r.Bathrooms.HasValue) specs.Add($"{r.Bathrooms} bathrooms");
        if (r.CarSpaces.HasValue) specs.Add($"{r.CarSpaces} car spaces");
        if (r.LandSize.HasValue) specs.Add($"{r.LandSize} sqm land");

        var specLine = specs.Count > 0 ? string.Join(", ", specs) : "Specs not yet supplied";
        var agentLine = string.IsNullOrWhiteSpace(r.AgentPhone)
            ? r.AgentName
            : $"{r.AgentName} ({r.AgentPhone})";

        return $"""
            You are the friendly Australian property enquiry concierge on unrealestate.au (not-for-profit, free forever) answering buyer calls for {r.Address}, {r.Suburb} {r.State} {r.Postcode}.

            VERIFIED LISTING FACTS (use only these facts — never guess or invent market claims):
            - Address: {r.Address}, {r.Suburb} {r.State} {r.Postcode}
            - Property type: {r.PropertyType}
            - Price guide: {(string.IsNullOrWhiteSpace(r.PriceDisplay) ? "Contact agent for price guide" : r.PriceDisplay)}
            - Specs: {specLine}
            - Description: {(string.IsNullOrWhiteSpace(r.Description) ? "No additional description supplied yet." : r.Description)}
            - Listing representative: {agentLine}

            YOUR JOB:
            1. Answer buyer questions concisely and warmly using only the verified facts above.
            2. If asked something not listed (rates, body corporate, building & pest, reserve price), say clearly you'll pass that straight to {r.AgentName} for a verified answer.
            3. Offer to book an inspection time or record an offer/callback — ask for the buyer's name, mobile number, and preferred inspection window or question.
            4. Keep responses crisp (1–3 sentences per turn) and hand off cleanly to {r.AgentName}.
            """;
    }

    public static string BuildCreateAgentPayload(ListingVoiceAssistantRequest request, string systemPrompt)
    {
        var agentDisplayName = $"unrealestate.au — {request.Address}, {request.Suburb}";
        if (agentDisplayName.Length > 96)
            agentDisplayName = agentDisplayName[..96];

        var firstMessage =
            $"G'day! You're speaking with the unrealestate.au assistant for {request.Address} in {request.Suburb}. " +
            $"Would you like details on the home, to book an inspection, or to leave a message for {request.AgentName}?";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("name", agentDisplayName);
            w.WritePropertyName("conversation_config");
            w.WriteStartObject();
            w.WritePropertyName("agent");
            w.WriteStartObject();
            w.WriteString("first_message", firstMessage);
            w.WriteString("language", "en");
            w.WritePropertyName("prompt");
            w.WriteStartObject();
            w.WriteString("prompt", systemPrompt);
            w.WriteEndObject();
            w.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(request.VoiceId))
            {
                w.WritePropertyName("tts");
                w.WriteStartObject();
                w.WriteString("voice_id", request.VoiceId.Trim());
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public async Task<VoiceAssistantProvisionResult> ProvisionListingAssistantAsync(
        ListingVoiceAssistantRequest request,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildListingSystemPrompt(request);

        if (!_routingClient.TryResolveConfig(out var cfg, out var configError) || cfg is null)
        {
            return new VoiceAssistantProvisionResult(
                Succeeded: false,
                AgentId: null,
                ProviderHost: null,
                SystemPromptPreview: systemPrompt,
                ErrorCode: "sovrgn_not_configured",
                Message: configError ?? "Set SOVRGN_API_KEY (tenant: aigents) to provision a live ElevenLabs voice assistant via api.sovrgn.ai/v1/convai.");
        }

        var uri = BuildConvaiUri(cfg.EndpointUri.AbsoluteUri, "agents/create");
        if (uri is null)
        {
            return new VoiceAssistantProvisionResult(
                Succeeded: false,
                AgentId: null,
                ProviderHost: cfg.EndpointUri.Host,
                SystemPromptPreview: systemPrompt,
                ErrorCode: "invalid_convai_uri",
                Message: "Could not derive a valid HTTPS /v1/convai/agents/create URL.");
        }

        var payload = BuildCreateAgentPayload(request, systemPrompt);
        var client = _httpClientFactory.CreateClient(SovrgnRoutingClient.HttpClientName);

        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        req.Headers.TryAddWithoutValidation("X-Sovrgn-Tenant", cfg.Tenant);

        try
        {
            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var errCode = TryExtractErrorCode(body) ?? $"http_{(int)resp.StatusCode}";
                _logger.LogWarning(
                    "Sovrgn ConvAI agent create returned HTTP {Status} ({Code}) from {Host}",
                    (int)resp.StatusCode, errCode, uri.Host);

                var friendly = (int)resp.StatusCode switch
                {
                    503 => "Voice assistant surface is not yet enabled on api.sovrgn.ai (upstream ElevenLabs key not configured).",
                    403 => "Voice assistant request blocked by residency or tenant policy on api.sovrgn.ai.",
                    401 => "SOVRGN_API_KEY was rejected by api.sovrgn.ai.",
                    _ => $"api.sovrgn.ai returned HTTP {(int)resp.StatusCode} while provisioning the voice assistant."
                };

                return new VoiceAssistantProvisionResult(
                    Succeeded: false,
                    AgentId: null,
                    ProviderHost: uri.Host,
                    SystemPromptPreview: systemPrompt,
                    ErrorCode: errCode,
                    Message: friendly);
            }

            string? agentId = null;
            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.TryGetProperty("agent_id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    agentId = idProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(agentId))
            {
                return new VoiceAssistantProvisionResult(
                    Succeeded: false,
                    AgentId: null,
                    ProviderHost: uri.Host,
                    SystemPromptPreview: systemPrompt,
                    ErrorCode: "missing_agent_id",
                    Message: "Upstream created response did not include an agent_id.");
            }

            return new VoiceAssistantProvisionResult(
                Succeeded: true,
                AgentId: agentId,
                ProviderHost: uri.Host,
                SystemPromptPreview: systemPrompt,
                ErrorCode: null,
                Message: $"ElevenLabs voice assistant provisioned via {uri.Host} (agent_id: {agentId}).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return new VoiceAssistantProvisionResult(
                Succeeded: false,
                AgentId: null,
                ProviderHost: uri.Host,
                SystemPromptPreview: systemPrompt,
                ErrorCode: "invalid_upstream_json",
                Message: $"{uri.Host} returned an unreadable response while provisioning the voice assistant.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed calling Sovrgn ConvAI endpoint at {Host}", uri.Host);
            return new VoiceAssistantProvisionResult(
                Succeeded: false,
                AgentId: null,
                ProviderHost: uri.Host,
                SystemPromptPreview: systemPrompt,
                ErrorCode: "upstream_unreachable",
                Message: $"Could not reach {uri.Host} to provision the voice assistant.");
        }
    }

    public async Task<VoiceConversationSessionResult> GetSignedConversationUrlAsync(
        string agentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return new VoiceConversationSessionResult(false, null, "invalid_agent_id", "An agent_id is required.");

        if (!_routingClient.TryResolveConfig(out var cfg, out var configError) || cfg is null)
            return new VoiceConversationSessionResult(false, null, "sovrgn_not_configured", configError ?? "SOVRGN_API_KEY is not configured.");

        var uri = BuildConvaiUri(cfg.EndpointUri.AbsoluteUri, $"conversation/get-signed-url?agent_id={Uri.EscapeDataString(agentId.Trim())}");
        if (uri is null)
            return new VoiceConversationSessionResult(false, null, "invalid_convai_uri", "Invalid Sovrgn ConvAI URL.");

        var client = _httpClientFactory.CreateClient(SovrgnRoutingClient.HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        req.Headers.TryAddWithoutValidation("X-Sovrgn-Tenant", cfg.Tenant);

        try
        {
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new VoiceConversationSessionResult(
                    false,
                    null,
                    $"http_{(int)resp.StatusCode}",
                    $"api.sovrgn.ai returned HTTP {(int)resp.StatusCode} requesting a signed voice session.");
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var signedUrl = doc.RootElement.TryGetProperty("signed_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;

            if (!IsValidSignedVoiceUrl(signedUrl))
                return new VoiceConversationSessionResult(false, null, "invalid_signed_url", "Upstream did not return a valid wss:// or https:// signed_url.");

            return new VoiceConversationSessionResult(true, signedUrl!.Trim(), null, "Voice session ready.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return new VoiceConversationSessionResult(false, null, "invalid_upstream_json", $"Unreadable JSON response from {uri.Host}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed requesting signed voice session from {Host}", uri.Host);
            return new VoiceConversationSessionResult(false, null, "upstream_unreachable", $"Could not reach {uri.Host} to start a voice session.");
        }
    }

    private static string? TryExtractErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.Object
                && err.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch
        {
            // Ignore non-JSON error payloads.
        }
        return null;
    }
}
