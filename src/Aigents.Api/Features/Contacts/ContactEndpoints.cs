using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Contacts;

/// <summary>
/// API endpoints for Contact management - used by mobile app
/// </summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/contacts")
            .WithTags("Contacts")
            .RequireAuthorization();

        // Get all contacts for agent (paginated)
        group.MapGet("/", GetContacts)
            .WithName("GetContacts")
            .WithSummary("Get agent's contacts with filtering and pagination");

        // Get single contact with full history
        group.MapGet("/{contactId}", GetContact)
            .WithName("GetContact")
            .WithSummary("Get contact details including calls and inspections");

        // Search contacts
        group.MapGet("/search", SearchContacts)
            .WithName("SearchContacts")
            .WithSummary("Search contacts by name, phone, or email");

        // Create contact
        group.MapPost("/", CreateContact)
            .WithName("CreateContact")
            .WithSummary("Create a new contact");

        // Update contact
        group.MapPut("/{contactId}", UpdateContact)
            .WithName("UpdateContact")
            .WithSummary("Update contact details");

        // Add note to contact
        group.MapPost("/{contactId}/notes", AddNote)
            .WithName("AddContactNote")
            .WithSummary("Add a note to a contact");

        // Get contact timeline
        group.MapGet("/{contactId}/timeline", GetTimeline)
            .WithName("GetContactTimeline")
            .WithSummary("Get all interactions with a contact");

        // Link contact to property
        group.MapPost("/{contactId}/properties/{propertyId}", LinkProperty)
            .WithName("LinkContactToProperty")
            .WithSummary("Link a contact to a property they're interested in");

        // Score contact
        group.MapPost("/{contactId}/score", ScoreLead)
            .WithName("ScoreContactLead")
            .WithSummary("AI score a contact's lead quality");

        return routes;
    }

    // ═══════════════════════════════════════════════════════════════
    // ENDPOINT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private static Task<IResult> GetContacts(
        string agentId,
        string? status = null,
        string? classification = null,
        string? sort = "lastContact",
        int page = 1,
        int pageSize = 50)
    {
        // TODO: Query from database
        var contacts = new List<ContactListItem>();

        return Task.FromResult(Results.Ok(new
        {
            contacts,
            page,
            pageSize,
            totalItems = 0,
            totalPages = 0
        }));
    }

    private static Task<IResult> GetContact(string contactId)
    {
        // TODO: Query from database with includes
        var contact = new ContactDetailResponse
        {
            Id = contactId,
            FullName = "Sample Contact",
            Phone = "+61400000000",
            Classification = "Buyer",
            LeadStatus = "Qualified",
            LeadScore = 75,
            RecentCalls = new List<CallSummary>(),
            PropertyInterests = new List<PropertyInterestSummary>(),
            InspectionsAttended = new List<InspectionSummary>(),
            NoteHistory = new List<NoteSummary>()
        };

        return Task.FromResult(Results.Ok(contact));
    }

    private static Task<IResult> SearchContacts(
        string agentId,
        string query)
    {
        // TODO: Full-text search
        var results = new List<ContactListItem>();

        return Task.FromResult(Results.Ok(new { results }));
    }

    private static Task<IResult> CreateContact(
        CreateContactRequest request)
    {
        var contactId = Guid.NewGuid().ToString();

        // TODO: Save to database

        return Task.FromResult(Results.Created($"/api/contacts/{contactId}", new
        {
            id = contactId,
            success = true
        }));
    }

    private static Task<IResult> UpdateContact(
        string contactId,
        UpdateContactRequest request)
    {
        // TODO: Update in database

        return Task.FromResult(Results.Ok(new { success = true }));
    }

    private static Task<IResult> AddNote(
        string contactId,
        AddNoteRequest request)
    {
        var noteId = Guid.NewGuid().ToString();

        // TODO: Save note

        return Task.FromResult(Results.Ok(new
        {
            noteId,
            success = true,
            timestamp = DateTimeOffset.UtcNow
        }));
    }

    private static Task<IResult> GetTimeline(string contactId)
    {
        // TODO: Query all interactions
        var timeline = new List<TimelineItem>();

        return Task.FromResult(Results.Ok(new { timeline }));
    }

    private static Task<IResult> LinkProperty(
        string contactId,
        string propertyId,
        LinkPropertyRequest request)
    {
        // TODO: Create link

        return Task.FromResult(Results.Ok(new { success = true }));
    }

    private static Task<IResult> ScoreLead(string contactId)
    {
        // TODO: Call AI service
        var score = new
        {
            score = 75,
            tier = "qualified",
            reasoning = "Strong engagement, attended 2 inspections, pre-approved",
            suggestedActions = new[] { "Follow up this week", "Send new listings" }
        };

        return Task.FromResult(Results.Ok(score));
    }

    // ═══════════════════════════════════════════════════════════════
    // MODELS
    // ═══════════════════════════════════════════════════════════════

    public record ContactListItem
    {
        public required string Id { get; init; }
        public required string FullName { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? PhotoUrl { get; init; }
        public string? Classification { get; init; }
        public string? LeadStatus { get; init; }
        public int LeadScore { get; init; }
        public DateTimeOffset? LastContactDate { get; init; }
        public string? LastInteraction { get; init; }
    }

    public record ContactDetailResponse
    {
        public required string Id { get; init; }
        public required string FullName { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? PhotoUrl { get; init; }
        public string? Classification { get; init; }
        public string? LeadStatus { get; init; }
        public int LeadScore { get; init; }
        public string? Source { get; init; }
        public string? Notes { get; init; }
        public string? LastAiSummary { get; init; }
        public DateTimeOffset? FirstContactDate { get; init; }
        public DateTimeOffset? LastContactDate { get; init; }
        public List<CallSummary>? RecentCalls { get; init; }
        public List<PropertyInterestSummary>? PropertyInterests { get; init; }
        public List<InspectionSummary>? InspectionsAttended { get; init; }
        public List<NoteSummary>? NoteHistory { get; init; }
    }

    public record CallSummary(string Id, DateTimeOffset Date, int DurationSeconds, string? Summary);
    public record PropertyInterestSummary(string PropertyId, string Address, string InterestLevel);
    public record InspectionSummary(string InspectionId, string Address, DateTimeOffset Date);
    public record NoteSummary(string Id, string Content, DateTimeOffset CreatedAt);

    public record CreateContactRequest(
        string AgentId,
        string FullName,
        string? Phone = null,
        string? Email = null,
        string? Classification = null,
        string? Source = null,
        string? Notes = null
    );

    public record UpdateContactRequest(
        string? FullName = null,
        string? Phone = null,
        string? Email = null,
        string? Classification = null,
        string? LeadStatus = null,
        string? Notes = null
    );

    public record AddNoteRequest(string Content);

    public record LinkPropertyRequest(
        string InterestLevel = "Interested",
        string? Notes = null
    );

    public record TimelineItem
    {
        public required string Type { get; init; } // "call", "inspection", "note", "email"
        public required DateTimeOffset Timestamp { get; init; }
        public required string Summary { get; init; }
        public string? DetailId { get; init; }
    }
}
