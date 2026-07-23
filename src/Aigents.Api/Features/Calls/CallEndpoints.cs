using Aigents.Infrastructure.CrmIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Calls;

/// <summary>
/// API endpoints for Call Intelligence - used by mobile app
/// </summary>
public static class CallEndpoints
{
    public static IEndpointRouteBuilder MapCallEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/calls")
            .WithTags("Call Intelligence")
            .RequireAuthorization();

        // Identify incoming caller
        group.MapGet("/identify/{phoneNumber}", IdentifyCaller)
            .WithName("IdentifyCaller")
            .WithSummary("Identify caller by phone number, returns context for incoming call screen");

        // Log a completed call
        group.MapPost("/", LogCall)
            .WithName("LogCall")
            .WithSummary("Log a completed call with optional transcription");

        // Get call history for agent
        group.MapGet("/history", GetCallHistory)
            .WithName("GetCallHistory")
            .WithSummary("Get recent call history for an agent");

        // Get AI summary for a call
        group.MapPost("/{callId}/summarize", SummarizeCall)
            .WithName("SummarizeCall")
            .WithSummary("Generate AI summary and action items from call transcript");

        // Upload call recording
        group.MapPost("/{callId}/recording", UploadRecording)
            .WithName("UploadCallRecording")
            .WithSummary("Upload call recording audio for transcription")
            .DisableAntiforgery();

        return routes;
    }

    // ═══════════════════════════════════════════════════════════════
    // ENDPOINT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> IdentifyCaller(
        string phoneNumber,
        string agentId,
        ICrmIntegrationHub crmHub,
        CancellationToken ct)
    {
        // Look up contact in connected CRM
        var contact = await crmHub.FindContactByPhoneAsync(agentId, phoneNumber, ct);

        if (contact == null)
        {
            return Results.Ok(new CallerIdentifyResponse
            {
                Found = false,
                PhoneNumber = phoneNumber
            });
        }

        // Get properties they're interested in
        // TODO: Pull from our database of contact-property associations

        return Results.Ok(new CallerIdentifyResponse
        {
            Found = true,
            PhoneNumber = phoneNumber,
            ContactId = contact.ExternalId,
            Name = contact.FullName,
            Email = contact.Email,
            Classification = contact.Classification.ToString(),
            LastContactDate = contact.LastContactDate,
            // These would come from our enriched data
            InterestedProperties = [],
            LastNote = null,
            LeadScore = null
        });
    }

    private static async Task<IResult> LogCall(
        LogCallRequest request,
        ICrmIntegrationHub crmHub,
        CancellationToken ct)
    {
        // Create call record in our database
        var callId = Guid.NewGuid().ToString();

        // Log to CRM
        if (!string.IsNullOrEmpty(request.ContactId))
        {
            var activity = new CrmActivity
            {
                ContactId = request.ContactId,
                PropertyId = request.PropertyId,
                Type = ActivityType.Call,
                Subject = $"Call with {request.CallerName ?? request.PhoneNumber}",
                Description = request.Notes,
                Timestamp = request.StartTime,
                DurationSeconds = request.DurationSeconds
            };

            await crmHub.LogCallAsync(request.AgentId, activity, ct);
        }

        return Results.Ok(new
        {
            callId,
            success = true,
            message = "Call logged successfully"
        });
    }

    private static Task<IResult> GetCallHistory(
        string agentId,
        int page = 1,
        int pageSize = 20)
    {
        // TODO: Pull from our database
        var calls = new List<CallHistoryItem>
        {
            // Sample data structure
        };

        return Task.FromResult(Results.Ok(new
        {
            calls,
            page,
            pageSize,
            totalItems = 0
        }));
    }

    private static async Task<IResult> SummarizeCall(
        string callId,
        SummarizeCallRequest request,
        CancellationToken ct)
    {
        // TODO: Call AI service to summarize transcript
        // This would use Gemini to analyze the transcript

        var summary = new CallSummaryResponse
        {
            CallId = callId,
            Summary = "AI summary would go here based on transcript analysis",
            Sentiment = "Positive",
            ActionItems = new List<ActionItem>
            {
                new("Follow up on Thursday", DateTimeOffset.UtcNow.AddDays(2)),
                new("Send comparable sales", DateTimeOffset.UtcNow.AddDays(1))
            },
            PropertiesMentioned = new List<string>(),
            KeyPoints = new List<string>
            {
                "Interested in making an offer",
                "Budget around $1.85M",
                "Needs to sell current property first"
            }
        };

        await Task.CompletedTask;
        return Results.Ok(summary);
    }

    private static async Task<IResult> UploadRecording(
        string callId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "No file uploaded" });
        }

        // TODO: Upload to Azure Blob Storage
        // TODO: Trigger transcription with Azure Speech Services

        await Task.CompletedTask;
        return Results.Ok(new
        {
            callId,
            fileName = file.FileName,
            size = file.Length,
            status = "processing",
            message = "Recording uploaded, transcription in progress"
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // RESPONSE MODELS
    // ═══════════════════════════════════════════════════════════════

    public record CallerIdentifyResponse
    {
        public bool Found { get; init; }
        public required string PhoneNumber { get; init; }
        public string? ContactId { get; init; }
        public string? Name { get; init; }
        public string? Email { get; init; }
        public string? Classification { get; init; }
        public DateTimeOffset? LastContactDate { get; init; }
        public List<PropertyInterest>? InterestedProperties { get; init; }
        public string? LastNote { get; init; }
        public int? LeadScore { get; init; }
    }

    public record PropertyInterest
    {
        public required string PropertyId { get; init; }
        public required string Address { get; init; }
        public string? PriceDisplay { get; init; }
        public string? InterestLevel { get; init; }
        public DateTimeOffset? LastInteraction { get; init; }
    }

    public record LogCallRequest(
        string AgentId,
        string PhoneNumber,
        string Direction, // "incoming" or "outgoing"
        DateTimeOffset StartTime,
        int DurationSeconds,
        string? ContactId = null,
        string? CallerName = null,
        string? PropertyId = null,
        string? Notes = null,
        string? TranscriptUrl = null
    );

    public record CallHistoryItem
    {
        public required string CallId { get; init; }
        public required string PhoneNumber { get; init; }
        public string? ContactName { get; init; }
        public required string Direction { get; init; }
        public required DateTimeOffset StartTime { get; init; }
        public required int DurationSeconds { get; init; }
        public string? AiSummary { get; init; }
        public bool HasTranscript { get; init; }
    }

    public record SummarizeCallRequest(
        string? Transcript = null
    );

    public record CallSummaryResponse
    {
        public required string CallId { get; init; }
        public required string Summary { get; init; }
        public string? Sentiment { get; init; }
        public required List<ActionItem> ActionItems { get; init; }
        public List<string>? PropertiesMentioned { get; init; }
        public List<string>? KeyPoints { get; init; }
    }

    public record ActionItem(string Description, DateTimeOffset? DueDate);
}
