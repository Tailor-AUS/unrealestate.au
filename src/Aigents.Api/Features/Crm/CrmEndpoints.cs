using Aigents.Infrastructure.CrmIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Crm;

/// <summary>
/// API endpoints for CRM integration - used by mobile app
/// </summary>
public static class CrmEndpoints
{
    public static IEndpointRouteBuilder MapCrmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/crm")
            .WithTags("CRM Integration")
            .RequireAuthorization();

        // Get available CRM systems
        group.MapGet("/adapters", GetAvailableAdapters)
            .WithName("GetCrmAdapters")
            .WithSummary("Get list of supported CRM systems");

        // Test CRM connection
        group.MapPost("/test-connection", TestConnection)
            .WithName("TestCrmConnection")
            .WithSummary("Test connection to a CRM with provided credentials");

        // Import contacts from CRM
        group.MapPost("/import", ImportContacts)
            .WithName("ImportCrmContacts")
            .WithSummary("Import all contacts from agent's connected CRM");

        // Get upcoming inspections
        group.MapGet("/inspections", GetUpcomingInspections)
            .WithName("GetCrmInspections")
            .WithSummary("Get agent's upcoming inspections from their CRM");

        // Search properties
        group.MapGet("/properties/search", SearchProperties)
            .WithName("SearchCrmProperties")
            .WithSummary("Search properties by address in connected CRM");

        // Log activity
        group.MapPost("/activities", LogActivity)
            .WithName("LogCrmActivity")
            .WithSummary("Log a call or note to the agent's CRM");

        // Create task
        group.MapPost("/tasks", CreateTask)
            .WithName("CreateCrmTask")
            .WithSummary("Create a follow-up task in the agent's CRM");

        return routes;
    }

    // ═══════════════════════════════════════════════════════════════
    // ENDPOINT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetAvailableAdapters(ICrmIntegrationHub hub)
    {
        var adapters = hub.GetAvailableAdapters();
        return Results.Ok(new { adapters });
    }

    private static async Task<IResult> TestConnection(
        TestConnectionRequest request,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var credentials = new CrmCredentials
        {
            AgentId = request.AgentId,
            ApiKey = request.ApiKey,
            AccessToken = request.AccessToken,
            ClientId = request.ClientId,
            ClientSecret = request.ClientSecret
        };

        var result = await hub.TestConnectionAsync(request.CrmId, credentials, ct);

        if (result.Success)
        {
            return Results.Ok(new
            {
                success = true,
                agentName = result.AgentName,
                officeName = result.OfficeName
            });
        }

        return Results.BadRequest(new
        {
            success = false,
            error = result.ErrorMessage
        });
    }

    private static async Task<IResult> ImportContacts(
        ImportContactsRequest request,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var credentials = new CrmCredentials
        {
            AgentId = request.AgentId,
            ApiKey = request.ApiKey,
            AccessToken = request.AccessToken
        };

        var result = await hub.ImportContactsAsync(request.AgentId, request.CrmId, credentials, ct);

        return Results.Ok(new
        {
            success = result.Success,
            imported = result.ContactsImported,
            updated = result.ContactsUpdated,
            skipped = result.ContactsSkipped,
            durationMs = result.Duration.TotalMilliseconds,
            error = result.ErrorMessage
        });
    }

    private static async Task<IResult> GetUpcomingInspections(
        string agentId,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var inspections = await hub.GetUpcomingInspectionsAsync(agentId, ct);

        return Results.Ok(new
        {
            inspections = inspections.Select(i => new
            {
                id = i.ExternalId,
                propertyId = i.PropertyId,
                address = i.PropertyAddress,
                startTime = i.StartTime,
                endTime = i.EndTime,
                rsvpCount = i.RsvpCount
            })
        });
    }

    private static async Task<IResult> SearchProperties(
        string agentId,
        string query,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var properties = await hub.SearchPropertiesAsync(agentId, query, ct);

        return Results.Ok(new
        {
            properties = properties.Select(p => new
            {
                id = p.ExternalId,
                address = p.Address,
                suburb = p.Suburb,
                priceDisplay = p.PriceDisplay,
                bedrooms = p.Bedrooms,
                bathrooms = p.Bathrooms,
                status = p.Status.ToString()
            })
        });
    }

    private static async Task<IResult> LogActivity(
        LogActivityRequest request,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var activity = new CrmActivity
        {
            ContactId = request.ContactId,
            PropertyId = request.PropertyId,
            Type = Enum.Parse<ActivityType>(request.Type, ignoreCase: true),
            Subject = request.Subject,
            Description = request.Description,
            Timestamp = request.Timestamp ?? DateTimeOffset.UtcNow,
            DurationSeconds = request.DurationSeconds
        };

        await hub.LogCallAsync(request.AgentId, activity, ct);

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        ICrmIntegrationHub hub,
        CancellationToken ct)
    {
        var task = new CrmTask
        {
            ContactId = request.ContactId,
            PropertyId = request.PropertyId,
            Subject = request.Subject,
            Description = request.Description,
            DueDate = request.DueDate,
            Priority = Enum.Parse<TaskPriority>(request.Priority ?? "Normal", ignoreCase: true)
        };

        await hub.CreateFollowUpAsync(request.AgentId, task, ct);

        return Results.Ok(new { success = true });
    }

    // ═══════════════════════════════════════════════════════════════
    // REQUEST MODELS
    // ═══════════════════════════════════════════════════════════════

    public record TestConnectionRequest(
        string CrmId,
        string AgentId,
        string? ApiKey = null,
        string? AccessToken = null,
        string? ClientId = null,
        string? ClientSecret = null
    );

    public record ImportContactsRequest(
        string CrmId,
        string AgentId,
        string? ApiKey = null,
        string? AccessToken = null
    );

    public record LogActivityRequest(
        string AgentId,
        string Type,
        string Subject,
        string? ContactId = null,
        string? PropertyId = null,
        string? Description = null,
        DateTimeOffset? Timestamp = null,
        int? DurationSeconds = null
    );

    public record CreateTaskRequest(
        string AgentId,
        string Subject,
        string? ContactId = null,
        string? PropertyId = null,
        string? Description = null,
        DateTimeOffset? DueDate = null,
        string? Priority = null
    );
}
