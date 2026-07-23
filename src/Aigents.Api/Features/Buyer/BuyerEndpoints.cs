using Aigents.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aigents.Api.Features.Buyer;

/// <summary>
/// API endpoints for authenticated Buyer interactions (Watchlist, Alerts)
/// </summary>
public static class BuyerEndpoints
{
    public static IEndpointRouteBuilder MapBuyerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/buyer")
            .WithTags("Buyer")
            .RequireAuthorization();

        // ───────────────────────────────────────────────────────────────
        // SAVED PROPERTIES (WATCHLIST)
        // ───────────────────────────────────────────────────────────────

        // Get watchlist
        group.MapGet("/watchlist", GetWatchlist)
            .WithName("GetWatchlist")
            .WithSummary("Get saved properties");

        // Add to watchlist
        group.MapPost("/watchlist", AddToWatchlist)
            .WithName("AddToWatchlist")
            .WithSummary("Save a property to watchlist");

        // Remove from watchlist
        group.MapDelete("/watchlist/{id}", RemoveFromWatchlist)
            .WithName("RemoveFromWatchlist")
            .WithSummary("Remove a property from watchlist");

        // Update watchlist item status (e.g., Inspected)
        group.MapPut("/watchlist/{id}/status", UpdateWatchlistStatus)
            .WithName("UpdateWatchlistStatus")
            .WithSummary("Update status of a saved property");

        // ───────────────────────────────────────────────────────────────
        // SAVED SEARCHES (ALERTS)
        // ───────────────────────────────────────────────────────────────

        // Get saved searches
        group.MapGet("/searches", GetSavedSearches)
            .WithName("GetSavedSearches")
            .WithSummary("Get saved search alerts");

        // Create saved search
        group.MapPost("/searches", CreateSavedSearch)
            .WithName("CreateSavedSearch")
            .WithSummary("Create a new property alert");

        // Delete saved search
        group.MapDelete("/searches/{id}", DeleteSavedSearch)
            .WithName("DeleteSavedSearch")
            .WithSummary("Delete a saved search");

        return routes;
    }

    // ───────────────────────────────────────────────────────────────
    // HANDLERS
    // ───────────────────────────────────────────────────────────────

    private static Task<IResult> GetWatchlist(string? userId)
    {
        // TODO: Get from DB
        var items = new List<UserSavedProperty>();
        return Task.FromResult(Results.Ok(items));
    }

    private static Task<IResult> AddToWatchlist(SavePropertyRequest request)
    {
        // TODO: Save to DB
        return Task.FromResult(Results.Created($"/api/buyer/watchlist/{Guid.NewGuid()}", new { success = true }));
    }

    private static Task<IResult> RemoveFromWatchlist(string id)
    {
        // TODO: Delete from DB
        return Task.FromResult(Results.Ok(new { success = true }));
    }

    private static Task<IResult> UpdateWatchlistStatus(string id, UpdateStatusRequest request)
    {
        // TODO: Update DB
        return Task.FromResult(Results.Ok(new { success = true }));
    }

    private static Task<IResult> GetSavedSearches(string? userId)
    {
        // TODO: Get from DB
        var items = new List<UserSavedSearch>();
        return Task.FromResult(Results.Ok(items));
    }

    private static Task<IResult> CreateSavedSearch(CreateSearchRequest request)
    {
        // TODO: Save to DB
        return Task.FromResult(Results.Created($"/api/buyer/searches/{Guid.NewGuid()}", new { success = true }));
    }

    private static Task<IResult> DeleteSavedSearch(string id)
    {
        // TODO: Delete from DB
        return Task.FromResult(Results.Ok(new { success = true }));
    }

    // ───────────────────────────────────────────────────────────────
    // REQUEST MODELS
    // ───────────────────────────────────────────────────────────────

    public record SavePropertyRequest(
        string PropertyId,
        string Source,
        string Address,
        string? ImageUrl,
        decimal? PriceEstimate
    );

    public record UpdateStatusRequest(string Status, string? Notes);

    public record CreateSearchRequest(
        string Name,
        string? Suburb,
        string? State,
        string? Postcode,
        int? MinBedrooms,
        int? MinBathrooms,
        decimal? MinPrice,
        decimal? MaxPrice,
        string Frequency = "Instant"
    );
}
