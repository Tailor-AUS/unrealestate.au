using Aigents.Domain.Enquiries;
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
    /// <summary>
    /// Creates a listing owned strictly by an existing authenticated user (never auto-creates
    /// raw Identity rows or attaches to another user's email). Enforces DB column length limits.
    /// </summary>
    Task<Listing> CreateOwnedListingAsync(Guid ownerUserId, Listing listing);
    Task<Listing?> GetListingAsync(Guid id);
    /// <summary>
    /// Public buyer-facing detail lookup: returns ONLY published (<see cref="ListingStatus.Active"/>)
    /// listings without exposing private owner/inquiry collections.
    /// </summary>
    Task<Listing?> GetPublishedListingByIdAsync(Guid id);
    Task<List<Listing>> GetListingsForUserAsync(string userEmail);
    Task<List<Listing>> GetPublishedListingsForOwnerAsync(Guid ownerUserId, int take = 100);
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
    /// <summary>
    /// Owner-scoped enquiry feed: returns ONLY enquiries on listings owned by <paramref name="ownerUserId"/>.
    /// </summary>
    Task<List<ListingInquiry>> GetInquiriesForOwnerAsync(Guid ownerUserId, int take = 100);
    /// <summary>
    /// Owner-scoped enquiry status update: mutates ONLY if the enquiry belongs to a listing owned by <paramref name="ownerUserId"/>.
    /// </summary>
    Task<bool> UpdateInquiryStatusForOwnerAsync(Guid inquiryId, Guid ownerUserId, InquiryStatus status);
    Task UpdateListingAsync(Listing listing);
}

/// <summary>
/// Database-backed listing service using Entity Framework Core.
/// </summary>
public class ListingService : IListingService
{
    private const string DirectBuyerProxyEmail = "buyer-enquiries@unrealestate.au";

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

        ClampListingColumns(listing);
        listing.CreatedAt = DateTime.UtcNow;
        listing.UpdatedAt = DateTime.UtcNow;

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

    public async Task<Listing> CreateOwnedListingAsync(Guid ownerUserId, Listing listing)
    {
        var ownerExists = await _db.Users.AnyAsync(u => u.Id == ownerUserId);
        if (!ownerExists)
            throw new InvalidOperationException("Authenticated user account was not found.");

        if (listing.Id == Guid.Empty)
            listing.Id = Guid.NewGuid();

        ClampListingColumns(listing);
        listing.UserId = ownerUserId;
        listing.User = null!;
        listing.CreatedAt = DateTime.UtcNow;
        listing.UpdatedAt = DateTime.UtcNow;

        _db.Listings.Add(listing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created owned listing {ListingId} for user {UserId}", listing.Id, ownerUserId);
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

    public async Task<List<Listing>> GetPublishedListingsForOwnerAsync(Guid ownerUserId, int take = 100)
    {
        return await _db.Listings
            .AsNoTracking()
            .Where(l => l.UserId == ownerUserId && l.Status == ListingStatus.Active)
            .OrderByDescending(l => l.PublishedAt ?? l.CreatedAt)
            .Take(Math.Clamp(take, 1, 250))
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

        // Reuse a single canonical proxy Agent row for direct buyer enquiries so PostgreSQL's
        // unique index IX_Agents_Email is never violated when multiple buyers enquire.
        var proxyAgentId = await ResolveDirectBuyerProxyAgentIdAsync();

        if (inquiry.Id == Guid.Empty) inquiry.Id = Guid.NewGuid();
        inquiry.ListingId = listingId;
        inquiry.AgentId = proxyAgentId;
        inquiry.Agent = null!;
        inquiry.Message = Truncate(inquiry.Message, EnquiryRecordCodec.MaxMessageLength);
        if (inquiry.CreatedAt == default) inquiry.CreatedAt = DateTime.UtcNow;

        _db.Set<ListingInquiry>().Add(inquiry);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ListingInquiry>> GetInquiriesForOwnerAsync(Guid ownerUserId, int take = 100)
    {
        return await _db.Set<ListingInquiry>()
            .AsNoTracking()
            .Include(i => i.Listing)
            .Include(i => i.Agent)
            .Where(i => i.Listing.UserId == ownerUserId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(Math.Clamp(take, 1, 250))
            .ToListAsync();
    }

    public async Task<bool> UpdateInquiryStatusForOwnerAsync(Guid inquiryId, Guid ownerUserId, InquiryStatus status)
    {
        var inquiry = await _db.Set<ListingInquiry>()
            .Include(i => i.Listing)
            .FirstOrDefaultAsync(i => i.Id == inquiryId && i.Listing.UserId == ownerUserId);
        if (inquiry is null) return false;

        inquiry.Status = status;
        if (status is InquiryStatus.Responded or InquiryStatus.InspectionBooked or InquiryStatus.OfferMade)
            inquiry.RespondedAt ??= DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateListingAsync(Listing listing)
    {
        ClampListingColumns(listing);
        listing.UpdatedAt = DateTime.UtcNow;

        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated listing {ListingId}", listing.Id);
    }

    private async Task<Guid> ResolveDirectBuyerProxyAgentIdAsync()
    {
        var existing = await _db.Set<Agent>()
            .Where(a => a.Email == DirectBuyerProxyEmail)
            .Select(a => a.Id)
            .FirstOrDefaultAsync();
        if (existing != Guid.Empty)
            return existing;

        var proxy = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Direct Buyer Enquiry",
            Email = DirectBuyerProxyEmail,
            Phone = ""
        };
        _db.Set<Agent>().Add(proxy);
        try
        {
            await _db.SaveChangesAsync();
            return proxy.Id;
        }
        catch (DbUpdateException)
        {
            // Handle concurrent first-insert race cleanly.
            _db.Entry(proxy).State = EntityState.Detached;
            return await _db.Set<Agent>()
                .Where(a => a.Email == DirectBuyerProxyEmail)
                .Select(a => a.Id)
                .FirstAsync();
        }
    }

    private static void ClampListingColumns(Listing l)
    {
        l.Address = Truncate(l.Address, 300);
        l.Suburb = Truncate(l.Suburb, 100);
        l.State = Truncate(l.State, 10);
        l.Postcode = Truncate(l.Postcode, 10);
        l.PropertyType = Truncate(string.IsNullOrWhiteSpace(l.PropertyType) ? "House" : l.PropertyType, 40);
        l.Headline = Truncate(l.Headline, 200);
        l.PriceDisplay = Truncate(l.PriceDisplay, 100);
        l.Description = Truncate(l.Description, 4000);
    }

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLen ? value : value[..maxLen];
    }
}
