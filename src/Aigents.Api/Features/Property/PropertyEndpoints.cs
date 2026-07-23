using Aigents.Domain.Entities;
using Aigents.Infrastructure.Growth;
using Aigents.Infrastructure.PropertyData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Aigents.Api.Features.Property;

/// <summary>
/// API endpoints for Buyer persona property search and data
/// </summary>
public static class PropertyEndpoints
{
    public static IEndpointRouteBuilder MapPropertyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/properties")
            .WithTags("Properties");

        // Search properties - PUBLIC (no auth required for browsing)
        group.MapGet("/search", SearchProperties)
            .WithName("SearchProperties")
            .WithSummary("Search properties (On-market and Off-market)")
            .AllowAnonymous();

        // Get property details - requires auth
        group.MapGet("/{id}", GetPropertyDetails)
            .WithName("GetPropertyDetails")
            .WithSummary("Get full property details including AVM")
            .RequireAuthorization();

        // Suburb insights - requires auth
        group.MapGet("/stats/{state}/{suburb}", GetSuburbStats)
            .WithName("GetSuburbStats")
            .WithSummary("Get market stats for a suburb")
            .RequireAuthorization();

        // Property Intelligence - PUBLIC (for listing wizard)
        group.MapPost("/intelligence", GetPropertyIntelligence)
            .WithName("GetPropertyIntelligence")
            .WithSummary("Get AI-powered property intelligence from web research")
            .AllowAnonymous();

        // AI-Powered Property Intelligence (Azure Foundry) - PUBLIC
        group.MapGet("/intelligence/ai", GetAiPropertyIntelligence)
            .WithName("GetAiPropertyIntelligence")
            .WithSummary("Get real-time property intelligence using Azure AI Foundry")
            .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> SearchProperties(
        IPropertyDataService propertyService,
        IProductEventRecorder productEvents,
        ClaimsPrincipal principal,
        string? query,
        string? suburb,
        string? state,
        int? minBedrooms,
        int? minBathrooms,
        decimal? minPrice,
        decimal? maxPrice,
        bool includeOffMarket = false,
        int page = 1)
    {
        var filter = new PropertySearchFilter
        {
            Query = query,
            Suburb = suburb,
            State = state,
            MinBedrooms = minBedrooms,
            MinBathrooms = minBathrooms,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            IncludeOffMarket = includeOffMarket,
            Page = page,
            PageSize = 20
        };

        var results = await propertyService.SearchPropertiesAsync(filter);
        await RecordForAuthenticatedUserAsync(
            productEvents,
            principal,
            ProductEventNames.SearchPerformed,
            deduplicationWindow: TimeSpan.FromMinutes(5));
        return Results.Ok(results);
    }

    private static async Task<IResult> GetPropertyDetails(
        IPropertyDataService propertyService,
        IProductEventRecorder productEvents,
        ClaimsPrincipal principal,
        string id)
    {
        var property = await propertyService.GetPropertyDetailsAsync(id);
        if (property == null)
        {
            return Results.NotFound(new { error = "Property not found" });
        }

        // Enrich with extra data (e.g. AVM if it's a listing)
        property = await propertyService.EnrichPropertyDataAsync(property);
        await RecordForAuthenticatedUserAsync(
            productEvents,
            principal,
            ProductEventNames.ListingViewed,
            Guid.TryParse(id, out var listingId) ? listingId : null,
            TimeSpan.FromMinutes(30));

        return Results.Ok(property);
    }

    private static async Task<IResult> GetSuburbStats(
        IPropertyDataService propertyService,
        IProductEventRecorder productEvents,
        ClaimsPrincipal principal,
        string state,
        string suburb)
    {
        var stats = await propertyService.GetSuburbInsightsAsync(suburb, state);
        if (stats == null)
        {
            return Results.NotFound(new { error = "Suburb not found" });
        }
        await RecordForAuthenticatedUserAsync(
            productEvents,
            principal,
            ProductEventNames.SearchPerformed,
            deduplicationWindow: TimeSpan.FromMinutes(5));
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetPropertyIntelligence(
        IPropertyIntelligenceService intelligenceService,
        PropertyIntelligenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return Results.BadRequest(new { error = "Address is required" });
        }

        var result = await intelligenceService.GetPropertyIntelligenceAsync(request);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAiPropertyIntelligence(
        Aigents.Infrastructure.Services.AI.IAiService aiService,
        string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return Results.BadRequest(new { error = "Address query parameter is required" });
        }

        var result = await aiService.SearchPropertyIntelligenceAsync(address);
        return Results.Ok(result);
    }

    private static Task<bool> RecordForAuthenticatedUserAsync(
        IProductEventRecorder productEvents,
        ClaimsPrincipal principal,
        string eventName,
        Guid? listingId = null,
        TimeSpan? deduplicationWindow = null)
    {
        return Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? productEvents.RecordAsync(
                userId,
                eventName,
                listingId,
                deduplicationWindow)
            : Task.FromResult(false);
    }
}
