using Aigents.Domain.Entities;
using Aigents.Infrastructure.PropertyData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Seller;

/// <summary>
/// API endpoints for Seller persona (Property Tracking, Appraisals)
/// </summary>
public static class SellerEndpoints
{
    public static IEndpointRouteBuilder MapSellerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/seller")
            .WithTags("Seller")
            .RequireAuthorization();

        // ───────────────────────────────────────────────────────────────
        // MY PROPERTIES
        // ───────────────────────────────────────────────────────────────

        // Get claimed properties
        group.MapGet("/properties", GetMyProperties)
            .WithName("GetSellerProperties")
            .WithSummary("Get properties claimed by the user");

        // Claim a property
        group.MapPost("/properties/claim", ClaimProperty)
            .WithName("ClaimProperty")
            .WithSummary("Claim ownership of a property");

        // Get dashboard stats (AVM + Market)
        group.MapGet("/properties/{id}/dashboard", GetPropertyDashboard)
            .WithName("GetPropertyDashboard")
            .WithSummary("Get dashboard stats for a claimed property");

        // ───────────────────────────────────────────────────────────────
        // APPRAISALS
        // ───────────────────────────────────────────────────────────────

        // Request appraisal
        group.MapPost("/properties/{id}/appraisals", RequestAppraisal)
            .WithName("RequestAppraisal")
            .WithSummary("Request an appraisal from an agent");

        // Get matching agents
        group.MapGet("/properties/{id}/agents", FindLocalAgents)
            .WithName("FindLocalAgents")
            .WithSummary("Find top performing agents for this property");

        return routes;
    }

    // ───────────────────────────────────────────────────────────────
    // HANDLERS
    // ───────────────────────────────────────────────────────────────

    private static Task<IResult> GetMyProperties(string? userId)
    {
        // TODO: DB Query
        var properties = new List<SellerPropertyListItem>();
        return Task.FromResult(Results.Ok(properties));
    }

    private static async Task<IResult> ClaimProperty(
        IPropertyDataService propertyService,
        ClaimPropertyRequest request)
    {
        // 1. Validate property exists
        var property = await propertyService.GetPropertyDetailsAsync(request.PropertyId)
            ?? await propertyService.GetByAddressAsync(request.Address); // Fallback if Address provided

        if (property == null)
        {
            return Results.BadRequest(new { error = "Property not found" });
        }

        // 2. TODO: Verify ownership (mocked as success)

        // 3. TODO: Save to DB

        return Results.Created($"/api/seller/properties/{Guid.NewGuid()}", new
        {
            success = true,
            avm = property.EstimatedValue
        });
    }

    private static async Task<IResult> GetPropertyDashboard(
        IPropertyDataService propertyService,
        string id)
    {
        // TODO: Get SellerProperty from DB
        // For now, mock fetching it and then enriching
        var sellerProperty = new SellerProperty
        {
            PropertyId = "mock-id",
            Address = "123 Sample St, Brisbane",
            Suburb = "Brisbane",
            LatestAvm = 1200000
        };

        // Fetch live market data
        var suburbProfile = await propertyService.GetSuburbInsightsAsync(sellerProperty.Suburb, "QLD");

        // Fetch updated AVM from CoreLogic (mock)
        var freshData = await propertyService.GetPropertyDetailsAsync(sellerProperty.PropertyId);

        return Results.Ok(new
        {
            property = sellerProperty,
            market = suburbProfile,
            liveAvm = freshData?.EstimatedValue ?? sellerProperty.LatestAvm,
            avmConfidence = freshData?.EstimatedValueConfidence ?? "Medium",
            recentSales = new[]
            {
                new { address = "5 Neighbor St", price = 1250000, date = DateTime.UtcNow.AddMonths(-1) }
            }
        });
    }

    private static Task<IResult> RequestAppraisal(
        string id,
        AppraisalRequestDto request)
    {
        // TODO: Save to DB
        // TODO: Notify Agent (Integration Event)

        return Task.FromResult(Results.Ok(new { success = true, id = Guid.NewGuid() }));
    }

    private static Task<IResult> FindLocalAgents(string id)
    {
        // TODO: Logic to find top agents in suburb based on sales data
        var agents = new[]
        {
            new {
                id = Guid.NewGuid(),
                name = "Sarah Smith",
                agency = "Ray White Brisbane",
                recentSales = 12,
                avgPrice = 1100000,
                rating = 4.9
            },
            new {
                id = Guid.NewGuid(),
                name = "Tom Jones",
                agency = "Place Estate Agents",
                recentSales = 8,
                avgPrice = 1400000,
                rating = 4.8
            }
        };

        return Task.FromResult(Results.Ok(agents));
    }

    // ───────────────────────────────────────────────────────────────
    // MODELS
    // ───────────────────────────────────────────────────────────────

    public record ClaimPropertyRequest(string PropertyId, string Address);
    public record AppraisalRequestDto(string Type, string? AgentId, string? Notes);

    public record SellerPropertyListItem
    {
        public required string Id { get; init; }
        public required string Address { get; init; }
        public decimal? CurrentValue { get; init; }
        public decimal? GrowthSincePurchase { get; init; }
    }
}

// Extension needed as GetByAddressAsync isn't on the service interface, only the provider interface
// We should add it to IPropertyDataService or cast. 
// For now, let's fix the ClaimProperty handler to use available methods.
file static class Extensions
{
    public static Task<BuyerProperty?> GetByAddressAsync(this IPropertyDataService service, string address)
    {
        // This is a hack for the sample code; ideally add to interface
        // We'll rely on GetPropertyDetailsAsync with ID mostly.
        return Task.FromResult<BuyerProperty?>(null);
    }
}
