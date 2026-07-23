// ═══════════════════════════════════════════════════════════════
// PUBLISH LISTING FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Distribute listing to all agents covering that area
// Exclusive off-market opportunity!
// ═══════════════════════════════════════════════════════════════

using System.Security.Claims;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aigents.Api.Features.Listings;

// ───────────────────────────────────────────────────────────────
// ENDPOINT
// ───────────────────────────────────────────────────────────────

public class PublishListingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/listings/{listingId}/publish", async (
            Guid listingId,
            ClaimsPrincipal principal,
            ISender sender) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(
                new PublishListingCommand(listingId, userId));
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequireAuthorization()
        .WithName("PublishListing")
        .WithTags("Listings")
        .WithOpenApi()
        .Produces<PublishListingResponse>();
    }

    private static bool TryGetUserId(
        ClaimsPrincipal principal,
        out Guid userId) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId);
}

// ───────────────────────────────────────────────────────────────
// REQUEST / RESPONSE
// ───────────────────────────────────────────────────────────────

public record PublishListingCommand(
    Guid ListingId,
    Guid UserId) : IRequest<PublishListingResponse?>;

public record PublishListingResponse(
    Guid ListingId,
    string Status,
    int AgentsNotified,
    List<NotifiedAgentDto> Agents,
    DateTime PublishedAt,
    string Message
);

public record NotifiedAgentDto(
    string Name,
    string Agency,
    string Suburbs
);

// ───────────────────────────────────────────────────────────────
// HANDLER
// ───────────────────────────────────────────────────────────────

public class PublishListingHandler : IRequestHandler<PublishListingCommand, PublishListingResponse?>
{
    private readonly AigentsDbContext _db;
    private readonly ILogger<PublishListingHandler> _logger;

    public PublishListingHandler(AigentsDbContext db, ILogger<PublishListingHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PublishListingResponse?> Handle(PublishListingCommand request, CancellationToken ct)
    {
        var listing = await _db.Listings
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.ListingId
                    && candidate.UserId == request.UserId,
                ct);
        if (listing is null)
            return null;

        // Validate listing is ready
        if (!listing.AgreementSigned)
            throw new InvalidOperationException("Agreement must be signed before publishing");

        if (listing.PublishedAt.HasValue)
            throw new InvalidOperationException("Listing already published");

        // Publishing currently makes the listing public. Agent delivery remains
        // disabled until a real notification executor exists; never fabricate
        // agents or claim that messages were sent.
        listing.Status = ListingStatus.Active;
        listing.DistributedToAgents = false;
        listing.DistributedAt = null;
        listing.AgentsNotified = 0;
        listing.PublishedAt = DateTime.UtcNow;
        listing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Listing {ListingId} published publicly",
            listing.Id);

        return new PublishListingResponse(
            listing.Id,
            "Active",
            0,
            [],
            listing.PublishedAt!.Value,
            "Your listing is public. Agent notification delivery is not enabled yet."
        );
    }
}
