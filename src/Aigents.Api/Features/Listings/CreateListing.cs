// ═══════════════════════════════════════════════════════════════
// CREATE LISTING FEATURE - VERTICAL SLICE
// ═══════════════════════════════════════════════════════════════
// Enter your address → AI generates full property listing
// Like Facebook Marketplace but for real estate!
// ═══════════════════════════════════════════════════════════════

using System.Security.Claims;
using System.Text.Json;
using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Aigents.Infrastructure.Services.AI;
using Carter;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aigents.Api.Features.Listings;

// ───────────────────────────────────────────────────────────────
// ENDPOINT
// ───────────────────────────────────────────────────────────────

public class CreateListingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/listings", async (CreateListingRequest request, ClaimsPrincipal principal, ISender sender) =>
        {
            // UserId comes from the authenticated principal, never the request
            // body — a client must not be able to attribute a listing to another
            // account.
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(new CreateListingCommand(
                userId,
                request.Address,
                request.Suburb,
                request.Postcode,
                request.Bedrooms,
                request.Bathrooms,
                request.CarSpaces,
                request.LandSize,
                request.PropertyType
            ));

            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithName("CreateListing")
        .WithTags("Listings")
        .WithOpenApi()
        .Produces<CreateListingResponse>();
    }
}

// ───────────────────────────────────────────────────────────────
// REQUEST / RESPONSE
// ───────────────────────────────────────────────────────────────

public record CreateListingRequest(
    string Address,
    string Suburb,
    string Postcode,
    int? Bedrooms = null,
    int? Bathrooms = null,
    int? CarSpaces = null,
    int? LandSize = null,
    string PropertyType = "House"
);

public record CreateListingResponse(
    Guid ListingId,
    string Address,
    string Headline,
    string Description,
    List<string> Features,
    string PriceDisplay,
    decimal? EstimatedValue,
    string TargetBuyers,
    string Status,
    string NextStep
);

// ───────────────────────────────────────────────────────────────
// COMMAND
// ───────────────────────────────────────────────────────────────

public record CreateListingCommand(
    Guid UserId,
    string Address,
    string Suburb,
    string Postcode,
    int? Bedrooms,
    int? Bathrooms,
    int? CarSpaces,
    int? LandSize,
    string PropertyType
) : IRequest<CreateListingResponse>;

// ───────────────────────────────────────────────────────────────
// VALIDATOR
// ───────────────────────────────────────────────────────────────

public class CreateListingValidator : AbstractValidator<CreateListingCommand>
{
    public CreateListingValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Suburb).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Postcode).NotEmpty().Length(4);
        RuleFor(x => x.PropertyType)
            .Must(t => new[] { "House", "Unit", "Townhouse", "Land", "Apartment" }.Contains(t))
            .WithMessage("Invalid property type");
    }
}

// ───────────────────────────────────────────────────────────────
// HANDLER
// ───────────────────────────────────────────────────────────────

public class CreateListingHandler : IRequestHandler<CreateListingCommand, CreateListingResponse>
{
    private readonly AigentsDbContext _db;
    private readonly IAiService _aiService;
    private readonly IProductEventRecorder _productEvents;
    private readonly ILogger<CreateListingHandler> _logger;

    public CreateListingHandler(
        AigentsDbContext db,
        IAiService aiService,
        IProductEventRecorder productEvents,
        ILogger<CreateListingHandler> logger)
    {
        _db = db;
        _aiService = aiService;
        _productEvents = productEvents;
        _logger = logger;
    }

    public async Task<CreateListingResponse> Handle(CreateListingCommand request, CancellationToken ct)
    {
        // Build the AI prompt
        var propertyDetails = $"""
            Address: {request.Address}, {request.Suburb} {request.Postcode}
            Property Type: {request.PropertyType}
            Bedrooms: {request.Bedrooms?.ToString() ?? "Unknown"}
            Bathrooms: {request.Bathrooms?.ToString() ?? "Unknown"}
            Car Spaces: {request.CarSpaces?.ToString() ?? "Unknown"}
            Land Size: {(request.LandSize.HasValue ? $"{request.LandSize}sqm" : "Unknown")}
            """;

        var prompt = $$"""
            Generate a compelling real estate listing for this property. Be creative and professional.

            {{propertyDetails}}

            Respond in this exact JSON format:
            {
                "headline": "A catchy 10-word max headline",
                "description": "A 150-200 word compelling description highlighting lifestyle, location benefits, and potential. Use paragraphs.",
                "features": ["Feature 1", "Feature 2", "Feature 3", "Feature 4", "Feature 5"],
                "estimatedValue": 850000,
                "priceDisplay": "Offers Over $850,000",
                "targetBuyers": "Young professionals, first home buyers, or investors looking for strong rental yields in a growth corridor."
            }

            Base the estimated value on typical {{request.Suburb}} QLD market prices for a {{request.PropertyType}}.
            Only respond with valid JSON, no other text.
            """;

        // Call AI
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var aiResponse = await _aiService.ChatAsync(messages, "sell", ct);

        // Parse AI response
        ListingAiResponse? parsed;
        try
        {
            // Clean up response (remove markdown if present)
            var jsonContent = aiResponse.Content
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            parsed = JsonSerializer.Deserialize<ListingAiResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response, using defaults");
            parsed = new ListingAiResponse
            {
                Headline = $"Beautiful {request.PropertyType} in {request.Suburb}",
                Description = $"Discover this wonderful {request.PropertyType} located in the heart of {request.Suburb}. Contact us for more details.",
                Features = new List<string> { "Great Location", "Modern Design", "Close to Amenities" },
                EstimatedValue = 750000,
                PriceDisplay = "Contact Agent",
                TargetBuyers = "Buyers looking for quality property in a great location."
            };
        }

        // Create listing
        var listing = new Listing
        {
            UserId = request.UserId,
            Address = request.Address,
            Suburb = request.Suburb,
            State = "QLD",
            Postcode = request.Postcode,
            Bedrooms = request.Bedrooms,
            Bathrooms = request.Bathrooms,
            CarSpaces = request.CarSpaces,
            LandSize = request.LandSize,
            PropertyType = request.PropertyType,
            Headline = parsed?.Headline ?? "",
            Description = parsed?.Description ?? "",
            Features = JsonSerializer.Serialize(parsed?.Features ?? new List<string>()),
            TargetBuyers = parsed?.TargetBuyers ?? "",
            EstimatedValue = parsed?.EstimatedValue,
            PriceDisplay = parsed?.PriceDisplay ?? "Contact Agent",
            Status = ListingStatus.Draft
        };

        _db.Listings.Add(listing);
        await _db.SaveChangesAsync(ct);
        await _productEvents.RecordAsync(
            request.UserId,
            ProductEventNames.ListingCreated,
            listing.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "Listing created. Id: {ListingId}, Address: {Address}",
            listing.Id, listing.Address);

        return new CreateListingResponse(
            listing.Id,
            $"{listing.Address}, {listing.Suburb}",
            listing.Headline,
            listing.Description,
            parsed?.Features ?? new List<string>(),
            listing.PriceDisplay,
            listing.EstimatedValue,
            listing.TargetBuyers,
            listing.Status.ToString(),
            "Sign the Open Listing Agreement to publish your property"
        );
    }
}

internal class ListingAiResponse
{
    public string Headline { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Features { get; set; } = new();
    public decimal EstimatedValue { get; set; }
    public string PriceDisplay { get; set; } = "";
    public string TargetBuyers { get; set; } = "";
}
