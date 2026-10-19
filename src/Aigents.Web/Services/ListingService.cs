using Aigents.Domain.Entities;
using Aigents.Domain.Matching;
using Aigents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aigents.Web.Services;

public sealed record PublishedListingQueryResult(
    IReadOnlyList<Listing> Listings,
    int TotalCandidateCount,
    bool Truncated);

public interface IListingService
{
    Task<Listing> CreateListingAsync(Listing listing);
    Task<Listing?> GetListingAsync(Guid id);
    /// <summary>
    /// Public buyer-facing detail lookup: returns ONLY published (<see cref="ListingStatus.Active"/>)
    /// listings without exposing private owner/inquiry collections.
    /// </summary>
    Task<Listing?> GetPublishedListingByIdAsync(Guid id);
    Task<List<Listing>> GetListingsForUserAsync(string userEmail);
    Task<List<Listing>> GetAllActiveListingsAsync();
    /// <summary>Active (published) listings only — never drafts. Used for buyer matching.</summary>
    Task<List<Listing>> GetPublishedListingsAsync(int take = 200);
    /// <summary>
    /// Queries published (<see cref="ListingStatus.Active"/>) listings with hard buyer preferences
    /// (suburbs and property type, plus soft-criterion eligibility when no hard filter is set)
    /// pushed into the database query before ordering and pagination so older matching listings
    /// are never displaced by newer non-matching rows.
    /// </summary>
    Task<PublishedListingQueryResult> QueryPublishedListingsForMatcherAsync(BuyerPreferences prefs, int take = 200);
    Task<bool> AddPublishedListingInquiryAsync(Guid listingId, ListingInquiry inquiry);
    Task UpdateListingAsync(Listing listing);
}

/// <summary>
/// Database-backed listing service using Entity Framework Core.
/// Persists listings to Azure SQL via AigentsDbContext.
/// </summary>
public class ListingService : IListingService
{
    private readonly AigentsDbContext _db;
    private readonly ILogger<ListingService> _logger;

    public ListingService(AigentsDbContext db, ILogger<ListingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Listing> CreateListingAsync(Listing listing)
    {
        if (listing.Id == Guid.Empty)
        {
            listing.Id = Guid.NewGuid();
        }

        listing.CreatedAt = DateTime.UtcNow;
        listing.UpdatedAt = DateTime.UtcNow;

        // Handle the User relationship
        if (listing.User != null && !string.IsNullOrEmpty(listing.User.Email))
        {
            var normalized = listing.User.Email.ToUpperInvariant();
            var existingUser = await _db.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);

            if (existingUser != null)
            {
                listing.UserId = existingUser.Id;
                listing.User = existingUser;
            }
            else
            {
                if (listing.User.Id == Guid.Empty)
                {
                    listing.User.Id = Guid.NewGuid();
                }
                listing.User.UserName ??= listing.User.Email;
                listing.User.NormalizedUserName = listing.User.UserName?.ToUpperInvariant();
                listing.User.NormalizedEmail = listing.User.Email.ToUpperInvariant();
                listing.User.CreatedAt = DateTime.UtcNow;
                listing.UserId = listing.User.Id;
                _db.Users.Add(listing.User);
            }
        }

        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created listing {ListingId} for address {Address}",
            listing.Id, listing.Address);

        return listing;
    }

    public async Task<Listing?> GetListingAsync(Guid id)
    {
        return await _db.Listings
            .Include(l => l.User)
            .Include(l => l.Inquiries)
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<Listing?> GetPublishedListingByIdAsync(Guid id)
    {
        return await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == id && l.Status == ListingStatus.Active)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Listing>> GetListingsForUserAsync(string userEmail)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
            return new List<Listing>();

        var normalized = userEmail.ToUpperInvariant();

        return await _db.Listings
            .Include(l => l.User)
            .Where(l => l.User != null && l.User.NormalizedEmail == normalized)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Listing>> GetAllActiveListingsAsync()
    {
        return await _db.Listings
            .Include(l => l.User)
            .Where(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.Draft)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<List<Listing>> GetPublishedListingsAsync(int take = 200)
    {
        return await _db.Listings
            .AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active)
            .OrderByDescending(l => l.PublishedAt ?? l.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();
    }

    public async Task<PublishedListingQueryResult> QueryPublishedListingsForMatcherAsync(BuyerPreferences prefs, int take = 200)
    {
        var limit = Math.Clamp(take, 1, 500);
        IQueryable<Listing> query = _db.Listings
            .AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active);

        var suburbs = prefs.Suburbs?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList() ?? [];

        if (suburbs.Count > 0)
        {
            query = query.Where(l => l.Suburb != null && suburbs.Contains(l.Suburb.Trim().ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(prefs.PropertyType))
        {
            var typeNorm = prefs.PropertyType.Trim().ToLowerInvariant();
            query = query.Where(l => l.PropertyType != null && l.PropertyType.Trim().ToLower() == typeNorm);
        }

        // When no hard location/type filter was supplied, exclude rows that cannot possibly
        // earn positive credit on any of the soft criteria the buyer specified, so non-matching
        // rows cannot displace older matching listings before the limit is applied.
        if (suburbs.Count == 0 && string.IsNullOrWhiteSpace(prefs.PropertyType) && prefs.HasAny)
        {
            var minBeds = prefs.MinBedrooms;
            var minBaths = prefs.MinBathrooms;
            var minPrice = prefs.MinPrice;
            var maxPrice = prefs.MaxPrice;
            var stretchCeiling = maxPrice.HasValue ? maxPrice.Value * (1m + BuyerMatcher.PriceStretch) : (decimal?)null;

            query = query.Where(l =>
                (minBeds != null && l.Bedrooms != null && l.Bedrooms >= minBeds.Value - 1) ||
                (minBaths != null && l.Bathrooms != null && l.Bathrooms >= minBaths.Value - 1) ||
                ((minPrice != null || maxPrice != null) &&
                 ((l.AskingPrice ?? l.EstimatedValue) > 0) &&
                 (stretchCeiling == null || (l.AskingPrice ?? l.EstimatedValue) <= stretchCeiling.Value)));
        }

        var totalCandidateCount = await query.CountAsync();
        var listings = await query
            .OrderByDescending(l => l.PublishedAt ?? l.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return new PublishedListingQueryResult(
            Listings: listings,
            TotalCandidateCount: totalCandidateCount,
            Truncated: totalCandidateCount > listings.Count);
    }

    public async Task<bool> AddPublishedListingInquiryAsync(Guid listingId, ListingInquiry inquiry)
    {
        var exists = await _db.Listings
            .AnyAsync(l => l.Id == listingId && l.Status == ListingStatus.Active);
        if (!exists) return false;

        if (inquiry.Id == Guid.Empty) inquiry.Id = Guid.NewGuid();
        inquiry.ListingId = listingId;
        if (inquiry.CreatedAt == default) inquiry.CreatedAt = DateTime.UtcNow;

        _db.Set<ListingInquiry>().Add(inquiry);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateListingAsync(Listing listing)
    {
        listing.UpdatedAt = DateTime.UtcNow;

        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated listing {ListingId}", listing.Id);
    }
}
