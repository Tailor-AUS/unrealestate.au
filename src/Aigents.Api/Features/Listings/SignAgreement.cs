// ═══════════════════════════════════════════════════════════════
// SIGN AGREEMENT FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Sign the open listing agreement before publishing
// Allows any agent who finds a buyer to earn commission
// ═══════════════════════════════════════════════════════════════

using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Aigents.Api.Features.Listings;

// ───────────────────────────────────────────────────────────────
// ENDPOINT
// ───────────────────────────────────────────────────────────────

public class SignAgreementEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Get agreement text
        app.MapGet("/api/listings/{listingId}/agreement", async (
            Guid listingId,
            ClaimsPrincipal principal,
            ISender sender) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(new GetAgreementQuery(listingId, userId));
            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequireAuthorization()
        .WithName("GetAgreement")
        .WithTags("Listings")
        .WithOpenApi()
        .Produces<AgreementResponse>();

        // Sign agreement
        app.MapPost("/api/listings/{listingId}/sign", async (
            Guid listingId,
            SignAgreementRequest request,
            ClaimsPrincipal principal,
            ISender sender) =>
        {
            if (!TryGetUserId(principal, out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(new SignAgreementCommand(
                listingId,
                userId,
                request.FullName,
                request.Signature,
                request.AgreedToTerms,
                request.CommissionRate
            ));

            return result is not null ? Results.Ok(result) : Results.NotFound();
        })
        .RequireAuthorization()
        .WithName("SignAgreement")
        .WithTags("Listings")
        .WithOpenApi()
        .Produces<SignAgreementResponse>();
    }

    private static bool TryGetUserId(
        ClaimsPrincipal principal,
        out Guid userId) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId);
}

// ───────────────────────────────────────────────────────────────
// GET AGREEMENT
// ───────────────────────────────────────────────────────────────

public record GetAgreementQuery(
    Guid ListingId,
    Guid UserId) : IRequest<AgreementResponse?>;

public record AgreementResponse(
    Guid ListingId,
    string PropertyAddress,
    string AgreementText,
    string TermsAndConditions,
    decimal SuggestedCommission,
    bool AlreadySigned
);

public class GetAgreementHandler : IRequestHandler<GetAgreementQuery, AgreementResponse?>
{
    private readonly AigentsDbContext _db;

    public GetAgreementHandler(AigentsDbContext db) => _db = db;

    public async Task<AgreementResponse?> Handle(GetAgreementQuery request, CancellationToken ct)
    {
        var listing = await _db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.ListingId
                    && candidate.UserId == request.UserId,
                ct);
        if (listing is null) return null;

        var agreementText = $"""
            OPEN LISTING AGREEMENT
            
            Property: {listing.Address}, {listing.Suburb} {listing.State} {listing.Postcode}
            
            I, the owner of the above property, hereby authorize Aigents.au to:
            
            1. Share this property listing with registered real estate agents in my area
            2. Allow any authorized agent to market this property to potential buyers
            3. Pay the agreed commission to the agent who successfully introduces a buyer
            
            This is an OPEN LISTING agreement, meaning:
            - Multiple agents may market this property simultaneously
            - Only the agent who introduces the successful buyer earns commission
            - You retain the right to sell privately without commission
            - This agreement can be cancelled at any time with 24 hours notice
            
            Commission Structure:
            - Standard rate: 2.0% of sale price (negotiable)
            - Paid only upon successful settlement
            - Split: 100% to introducing agent
            
            By signing below, I confirm I am the legal owner or authorized representative
            of this property and have the authority to list it for sale.
            """;

        var termsAndConditions = """
            TERMS AND CONDITIONS
            
            1. AGENCY RELATIONSHIP
            - Aigents.au acts as a platform connecting sellers with agents
            - Aigents.au is not a licensed real estate agency
            - All agents on our platform are independently licensed
            
            2. COMMISSION
            - Commission is payable only upon successful settlement
            - The rate specified above is the total commission
            - Payment is made directly to the introducing agent's agency
            
            3. MARKETING
            - Your listing will be shared as an "off-market opportunity"
            - Agents may contact you directly via the platform
            - You control which inspections and offers to accept
            
            4. PRIVACY
            - Your contact details are shared only with verified agents
            - We comply with Australian Privacy Principles
            - You can request data deletion at any time
            
            5. CANCELLATION
            - Cancel your listing at any time via your dashboard
            - 24 hours notice required if agents have shown interest
            - No cancellation fees apply
            
            6. DISPUTES
            - Governed by Queensland law
            - Mediation required before legal action
            
            By signing, you agree to these terms and confirm you have read
            and understood the Open Listing Agreement above.
            """;

        return new AgreementResponse(
            listing.Id,
            $"{listing.Address}, {listing.Suburb}",
            agreementText,
            termsAndConditions,
            2.0m, // Default 2% commission
            listing.AgreementSigned
        );
    }
}

// ───────────────────────────────────────────────────────────────
// SIGN AGREEMENT
// ───────────────────────────────────────────────────────────────

public record SignAgreementRequest(
    string FullName,
    string Signature, // Can be typed name or base64 image
    bool AgreedToTerms,
    decimal CommissionRate = 2.0m
);

public record SignAgreementResponse(
    Guid ListingId,
    string Status,
    DateTime SignedAt,
    string NextStep
);

public record SignAgreementCommand(
    Guid ListingId,
    Guid UserId,
    string FullName,
    string Signature,
    bool AgreedToTerms,
    decimal CommissionRate
) : IRequest<SignAgreementResponse?>;

public class SignAgreementValidator : AbstractValidator<SignAgreementCommand>
{
    public SignAgreementValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Signature).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.AgreedToTerms).Equal(true).WithMessage("You must agree to the terms");
        RuleFor(x => x.CommissionRate).InclusiveBetween(1.0m, 5.0m);
    }
}

public class SignAgreementHandler : IRequestHandler<SignAgreementCommand, SignAgreementResponse?>
{
    private readonly AigentsDbContext _db;
    private readonly ILogger<SignAgreementHandler> _logger;

    public SignAgreementHandler(AigentsDbContext db, ILogger<SignAgreementHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SignAgreementResponse?> Handle(SignAgreementCommand request, CancellationToken ct)
    {
        var listing = await _db.Listings.FirstOrDefaultAsync(
            candidate => candidate.Id == request.ListingId
                && candidate.UserId == request.UserId,
            ct);
        if (listing is null)
            return null;

        if (listing.AgreementSigned)
            throw new InvalidOperationException("Agreement already signed");

        // Record the signature
        listing.AgreementSigned = true;
        listing.AgreementSignedAt = DateTime.UtcNow;
        listing.AgreementSignature = $"{request.FullName}|{request.Signature}";
        listing.Status = ListingStatus.PendingSignature; // Ready to publish
        listing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agreement signed for listing {ListingId}",
            listing.Id);

        return new SignAgreementResponse(
            listing.Id,
            "Agreement Signed",
            listing.AgreementSignedAt.Value,
            "Click 'Publish' to send your listing to local agents"
        );
    }
}
