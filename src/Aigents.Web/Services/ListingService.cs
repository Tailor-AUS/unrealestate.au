using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aigents.Web.Services;

public interface IListingService
{
    Task<Listing> CreateListingAsync(Listing listing);
    Task<Listing?> GetListingAsync(Guid id);
    Task<List<Listing>> GetListingsForUserAsync(string userEmail);
    Task<List<Listing>> GetAllActiveListingsAsync();
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
            // Check if user already exists. Identity persists NormalizedEmail so we
            // match against that rather than calling ToLower on the server side.
            var normalized = listing.User.Email.ToUpperInvariant();
            var existingUser = await _db.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);

            if (existingUser != null)
            {
                // Use existing user
                listing.UserId = existingUser.Id;
                listing.User = existingUser;
            }
            else
            {
                // Create new user (ghost user) — Identity will fill normalized fields
                // when the user later registers properly with a password.
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

    public async Task UpdateListingAsync(Listing listing)
    {
        listing.UpdatedAt = DateTime.UtcNow;
        
        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated listing {ListingId}", listing.Id);
    }
}
