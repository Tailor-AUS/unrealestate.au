using Aigents.Infrastructure.CrmIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Cryptography;
using System.Text;

namespace Aigents.Api.Features.Inspections;

/// <summary>
/// API endpoints for Digital Inspection Check-in
/// </summary>
public static class InspectionEndpoints
{
    public static IEndpointRouteBuilder MapInspectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/inspections")
            .WithTags("Inspections");

        // Agent endpoints (require auth)
        var agentGroup = group.MapGroup("")
            .RequireAuthorization();

        agentGroup.MapGet("/", GetAgentInspections)
            .WithName("GetAgentInspections")
            .WithSummary("Get all upcoming inspections for an agent");

        agentGroup.MapPost("/", CreateInspection)
            .WithName("CreateInspection")
            .WithSummary("Create a new inspection event");

        agentGroup.MapGet("/{inspectionId}/qr", GetQrCode)
            .WithName("GetInspectionQrCode")
            .WithSummary("Get QR code data for inspection check-in");

        agentGroup.MapGet("/{inspectionId}/attendees", GetAttendees)
            .WithName("GetInspectionAttendees")
            .WithSummary("Get all attendees who checked in to an inspection");

        // Public check-in endpoint (no auth required)
        group.MapPost("/checkin/{token}", CheckIn)
            .WithName("InspectionCheckIn")
            .WithSummary("Public endpoint for attendees to check in via QR code")
            .AllowAnonymous();

        // Get check-in form config (no auth required)
        group.MapGet("/checkin/{token}/config", GetCheckInConfig)
            .WithName("GetCheckInConfig")
            .WithSummary("Get check-in form configuration for an inspection")
            .AllowAnonymous();

        return routes;
    }

    // ═══════════════════════════════════════════════════════════════
    // ENDPOINT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> GetAgentInspections(
        string agentId,
        ICrmIntegrationHub crmHub,
        CancellationToken ct)
    {
        var inspections = await crmHub.GetUpcomingInspectionsAsync(agentId, ct);

        var result = inspections.Select(i => new InspectionResponse
        {
            InspectionId = i.ExternalId,
            PropertyId = i.PropertyId,
            Address = i.PropertyAddress ?? "Unknown",
            StartTime = i.StartTime,
            EndTime = i.EndTime,
            RsvpCount = i.RsvpCount ?? 0,
            CheckInCount = 0, // TODO: Pull from our database
            CheckInToken = GenerateCheckInToken(i.ExternalId),
            QrCodeUrl = $"/api/inspections/{i.ExternalId}/qr"
        });

        return Results.Ok(new { inspections = result });
    }

    private static Task<IResult> CreateInspection(
        CreateInspectionRequest request,
        CancellationToken ct)
    {
        // TODO: Create in our database
        var inspectionId = Guid.NewGuid().ToString();
        var token = GenerateCheckInToken(inspectionId);

        return Task.FromResult(Results.Ok(new
        {
            inspectionId,
            checkInToken = token,
            checkInUrl = $"https://unrealestate.au/checkin/{token}",
            qrCodeUrl = $"/api/inspections/{inspectionId}/qr"
        }));
    }

    private static Task<IResult> GetQrCode(string inspectionId)
    {
        var token = GenerateCheckInToken(inspectionId);
        var checkInUrl = $"https://unrealestate.au/checkin/{token}";

        // Return QR code configuration (mobile app will render it)
        return Task.FromResult(Results.Ok(new
        {
            inspectionId,
            checkInUrl,
            token,
            // Could also generate actual QR image URL
            qrDataUri = $"data:image/svg+xml,..." // Placeholder
        }));
    }

    private static Task<IResult> GetAttendees(string inspectionId)
    {
        // TODO: Pull from our database
        var attendees = new List<AttendeeResponse>
        {
            // Sample structure
        };

        return Task.FromResult(Results.Ok(new
        {
            inspectionId,
            totalCount = attendees.Count,
            attendees
        }));
    }

    private static Task<IResult> GetCheckInConfig(string token)
    {
        // Validate token and get inspection details
        // TODO: Look up inspection from token

        return Task.FromResult(Results.Ok(new CheckInConfigResponse
        {
            PropertyAddress = "45 Ocean Street, Manly",
            AgentName = "Sarah Smith",
            AgentPhoto = null,
            InspectionTime = "10:00 AM - 10:30 AM",
            Questions = new List<CheckInQuestion>
            {
                new("preApproved", "Are you pre-approved?", "choice",
                    new[] { "Yes", "No", "Working on it" }),
                new("timeline", "When are you looking to buy?", "choice",
                    new[] { "Right now", "1-3 months", "3-6 months", "Just browsing" }),
                new("budget", "What's your budget?", "choice",
                    new[] { "Under $1M", "$1M-$1.5M", "$1.5M-$2M", "$2M+" }),
                new("firstHome", "Is this your first home?", "choice",
                    new[] { "Yes", "No" })
            }
        }));
    }

    private static async Task<IResult> CheckIn(
        string token,
        CheckInRequest request,
        ICrmIntegrationHub crmHub,
        CancellationToken ct)
    {
        // Validate token
        // TODO: Look up inspection from token

        // Create/update contact in CRM
        if (request.AgentId != null)
        {
            var contact = new CrmContact
            {
                FullName = request.Name,
                Email = request.Email,
                Mobile = request.Phone,
                Classification = ContactClassification.Buyer,
                LeadSource = "Open Home Check-in"
            };

            // Note: Would need to get agent's CRM credentials
            // This is a simplified flow
        }

        // Store check-in in our database
        var checkInId = Guid.NewGuid().ToString();

        await Task.CompletedTask;

        return Results.Ok(new
        {
            success = true,
            checkInId,
            message = "Thanks for checking in! The agent will follow up with you.",
            propertyAddress = "45 Ocean Street, Manly"
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static string GenerateCheckInToken(string inspectionId)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{inspectionId}:aigents-secret"));
        return $"{inspectionId}:{Convert.ToBase64String(hash)[..8]}";
    }

    // ═══════════════════════════════════════════════════════════════
    // REQUEST/RESPONSE MODELS
    // ═══════════════════════════════════════════════════════════════

    public record CreateInspectionRequest(
        string AgentId,
        string PropertyId,
        string Address,
        DateTimeOffset StartTime,
        DateTimeOffset EndTime
    );

    public record InspectionResponse
    {
        public required string InspectionId { get; init; }
        public required string PropertyId { get; init; }
        public required string Address { get; init; }
        public required DateTimeOffset StartTime { get; init; }
        public required DateTimeOffset EndTime { get; init; }
        public int RsvpCount { get; init; }
        public int CheckInCount { get; init; }
        public required string CheckInToken { get; init; }
        public required string QrCodeUrl { get; init; }
    }

    public record AttendeeResponse
    {
        public required string CheckInId { get; init; }
        public required string Name { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public required DateTimeOffset CheckInTime { get; init; }
        public bool PreApproved { get; init; }
        public string? Timeline { get; init; }
        public string? Budget { get; init; }
        public bool OptedInAlerts { get; init; }
    }

    public record CheckInConfigResponse
    {
        public required string PropertyAddress { get; init; }
        public required string AgentName { get; init; }
        public string? AgentPhoto { get; init; }
        public required string InspectionTime { get; init; }
        public required List<CheckInQuestion> Questions { get; init; }
    }

    public record CheckInQuestion(
        string Id,
        string Label,
        string Type, // "text", "choice", "checkbox"
        string[]? Options = null
    );

    public record CheckInRequest(
        string Name,
        string Phone,
        string? Email = null,
        bool PreApproved = false,
        string? Timeline = null,
        string? Budget = null,
        bool OptInAlerts = false,
        Dictionary<string, string>? Answers = null,
        string? AgentId = null
    );
}
