// ═══════════════════════════════════════════════════════════════
// LEADS FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Handles lead capture and handoff to human agents.
// ═══════════════════════════════════════════════════════════════

using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aigents.Api.Features.Leads;

// ───────────────────────────────────────────────────────────────
// HANDOFF ENDPOINT
// ───────────────────────────────────────────────────────────────

public class HandoffLeadEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/leads/handoff", async (HandoffRequest request, ISender sender) =>
        {
            var result = await sender.Send(new HandoffCommand(
                request.UserId,
                request.ConversationId,
                request.Notes
            ));
            
            return Results.Ok(result);
        })
        .WithName("HandoffLead")
        .WithOpenApi()
        .Produces<HandoffResponse>();

        app.MapGet("/api/leads", async (ISender sender) =>
        {
            var result = await sender.Send(new GetLeadsQuery());
            return Results.Ok(result);
        })
        .WithName("GetLeads")
        .WithOpenApi()
        .Produces<List<LeadDto>>();
    }
}

// ───────────────────────────────────────────────────────────────
// HANDOFF REQUEST / RESPONSE
// ───────────────────────────────────────────────────────────────

public record HandoffRequest(Guid UserId, Guid ConversationId, string? Notes);

public record HandoffResponse(
    Guid LeadId,
    string UserName,
    string UserEmail,
    string Summary,
    string Status
);

public record HandoffCommand(Guid UserId, Guid ConversationId, string? Notes) 
    : IRequest<HandoffResponse>;

// ───────────────────────────────────────────────────────────────
// HANDOFF HANDLER
// ───────────────────────────────────────────────────────────────

public class HandoffHandler : IRequestHandler<HandoffCommand, HandoffResponse>
{
    private readonly AigentsDbContext _db;
    private readonly ILogger<HandoffHandler> _logger;

    public HandoffHandler(AigentsDbContext db, ILogger<HandoffHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HandoffResponse> Handle(HandoffCommand request, CancellationToken ct)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
            ?? throw new InvalidOperationException("User not found");

        var conversation = await _db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, ct)
            ?? throw new InvalidOperationException("Conversation not found");

        // Generate summary from conversation
        var userMessages = conversation.Messages
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content)
            .ToList();

        var summary = GenerateSummary(userMessages, conversation.Mode);
        
        if (!string.IsNullOrEmpty(request.Notes))
        {
            summary += $" Agent notes: {request.Notes}";
        }

        // Update user status
        user.Status = LeadStatus.HandedOff;
        user.HandedOffAt = DateTime.UtcNow;

        // Update conversation
        conversation.Status = ConversationStatus.HandedOff;
        conversation.Summary = summary;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Lead handed off. UserId: {UserId}, Name: {Name}",
            user.Id, user.Name);

        // TODO: Send notification to agent (email, SMS, Slack)
        // TODO: Create task in CRM

        return new HandoffResponse(
            user.Id,
            user.Name,
            user.Email ?? string.Empty,
            summary,
            "handed_off"
        );
    }

    private static string GenerateSummary(List<string> messages, AgentMode mode)
    {
        var combined = string.Join(" ", messages).ToLower();
        var summary = mode == AgentMode.Buy ? "Buyer inquiry. " : "Seller inquiry. ";

        if (combined.Contains("paddington")) summary += "Interested in Paddington. ";
        if (combined.Contains("bulimba")) summary += "Looking at Bulimba. ";
        if (combined.Contains("new farm")) summary += "Interested in New Farm. ";
        if (combined.Contains("gold coast") || combined.Contains("burleigh"))
            summary += "Gold Coast focus. ";
        if (combined.Contains("off-market")) summary += "Wants off-market opportunities. ";
        if (combined.Contains("investment") || combined.Contains("yield"))
            summary += "Investment focus. ";

        return summary.Trim();
    }
}

// ───────────────────────────────────────────────────────────────
// GET LEADS QUERY
// ───────────────────────────────────────────────────────────────

public record GetLeadsQuery : IRequest<List<LeadDto>>;

public record LeadDto(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    string Mode,
    string Status,
    int MessageCount,
    DateTime CreatedAt,
    DateTime? HandedOffAt
);

public class GetLeadsHandler : IRequestHandler<GetLeadsQuery, List<LeadDto>>
{
    private readonly AigentsDbContext _db;

    public GetLeadsHandler(AigentsDbContext db)
    {
        _db = db;
    }

    public async Task<List<LeadDto>> Handle(GetLeadsQuery request, CancellationToken ct)
    {
        return await _db.Users
            .Include(u => u.Conversations)
                .ThenInclude(c => c.Messages)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new LeadDto(
                u.Id,
                u.Name,
                u.Email ?? string.Empty,
                u.PhoneNumber,
                u.PreferredMode.ToString(),
                u.Status.ToString(),
                u.Conversations.SelectMany(c => c.Messages).Count(),
                u.CreatedAt,
                u.HandedOffAt
            ))
            .ToListAsync(ct);
    }
}
