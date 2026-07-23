using Aigents.Domain.Entities;
using Aigents.Infrastructure.Data;
using Aigents.Infrastructure.Growth;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace Aigents.Web.Services;

public interface IListingService
{
    Task<Listing> CreateListingAsync(Listing listing);
    Task<Listing?> GetPublicListingAsync(Guid id);
    Task<Listing?> GetListingForUserAsync(Guid id, Guid userId);
    Task<List<Listing>> GetListingsForUserAsync(Guid userId);
    Task<List<Listing>> GetAllActiveListingsAsync();
    Task<ListingInquiry> CreateInquiryAsync(Guid listingId, ListingInquiry inquiry);
    Task UpdateListingAsync(Listing listing);
}

/// <summary>
/// Database-backed listing service using Entity Framework Core.
/// Persists listings to PostgreSQL via AigentsDbContext.
/// </summary>
public class ListingService : IListingService
{
    private readonly AigentsDbContext _db;
    private readonly IProductEventRecorder _productEvents;
    private readonly ILogger<ListingService> _logger;

    public ListingService(
        AigentsDbContext db,
        IProductEventRecorder productEvents,
        ILogger<ListingService> logger)
    {
        _db = db;
        _productEvents = productEvents;
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
        if (listing.Status == ListingStatus.Active)
        {
            listing.PublishedAt ??= DateTime.UtcNow;
        }

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
        await _productEvents.RecordAsync(
            listing.UserId,
            ProductEventNames.ListingCreated,
            listing.Id);

        _logger.LogInformation("Created listing {ListingId} for address {Address}",
            listing.Id, listing.Address);

        return listing;
    }

    public async Task<Listing?> GetPublicListingAsync(Guid id)
    {
        return await _db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                listing => listing.Id == id && listing.Status == ListingStatus.Active);
    }

    public async Task<Listing?> GetListingForUserAsync(Guid id, Guid userId)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            return null;
        }

        return await _db.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                listing => listing.Id == id && listing.UserId == userId);
    }

    public async Task<List<Listing>> GetListingsForUserAsync(Guid userId)
    {
        if (userId == Guid.Empty)
            return new List<Listing>();

        return await _db.Listings
            .Include(l => l.Inquiries)
                .ThenInclude(inquiry => inquiry.Agent)
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Listing>> GetAllActiveListingsAsync()
    {
        return await _db.Listings
            .AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<ListingInquiry> CreateInquiryAsync(
        Guid listingId,
        ListingInquiry inquiry)
    {
        ValidateAndNormalizeInquiry(inquiry);

        var listingIsActive = await _db.Listings
            .AnyAsync(listing =>
                listing.Id == listingId
                && listing.Status == ListingStatus.Active);
        if (!listingIsActive)
        {
            throw new InvalidOperationException("The public listing is not available.");
        }

        inquiry.Id = inquiry.Id == Guid.Empty ? Guid.NewGuid() : inquiry.Id;
        inquiry.ListingId = listingId;
        inquiry.CreatedAt = DateTime.UtcNow;

        _db.ListingInquiries.Add(inquiry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Created {InquiryType} inquiry {InquiryId} for listing {ListingId}",
            inquiry.InquiryType,
            inquiry.Id,
            listingId);
        return inquiry;
    }

    private static void ValidateAndNormalizeInquiry(ListingInquiry inquiry)
    {
        inquiry.Message = (inquiry.Message ?? string.Empty).Trim();
        inquiry.BuyerName = inquiry.BuyerName?.Trim();
        inquiry.BuyerEmail = inquiry.BuyerEmail?.Trim();
        inquiry.BuyerPhone = inquiry.BuyerPhone?.Trim();

        if (inquiry.Message.Length is 0 or > 2000)
            throw new ArgumentException("Message must contain 1 to 2,000 characters.");
        if (inquiry.BuyerName?.Length > 255)
            throw new ArgumentException("Buyer name must be 255 characters or fewer.");
        if (inquiry.BuyerEmail?.Length > 255)
            throw new ArgumentException("Buyer email must be 255 characters or fewer.");
        if (inquiry.BuyerPhone?.Length > 32)
            throw new ArgumentException("Buyer phone must be 32 characters or fewer.");
        if (inquiry.BuyerEmail is { Length: > 0 } email
            && (!MailAddress.TryCreate(email, out var address)
                || !address.Address.Equals(email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Enter a valid buyer email address.");
        }

        var phoneDigits = inquiry.BuyerPhone?
            .Where(char.IsDigit)
            .Count() ?? 0;
        if (inquiry.BuyerPhone is { Length: > 0 }
            && (phoneDigits is < 8 or > 15
                || inquiry.BuyerPhone.Any(character =>
                    !char.IsDigit(character)
                    && !char.IsWhiteSpace(character)
                    && character is not ('+' or '-' or '(' or ')' or '.'))))
        {
            throw new ArgumentException("Enter a valid buyer phone number.");
        }

        switch (inquiry.InquiryType)
        {
            case BuyerInquiryType.Question
                when string.IsNullOrWhiteSpace(inquiry.BuyerEmail):
                throw new ArgumentException("An email address is required for a question.");
            case BuyerInquiryType.Inspection
                when string.IsNullOrWhiteSpace(inquiry.BuyerPhone):
                throw new ArgumentException("A phone number is required for an inspection.");
            case BuyerInquiryType.Offer
                when inquiry.OfferAmount is null or <= 0:
                throw new ArgumentException("Offer amount must be greater than zero.");
            case BuyerInquiryType.Offer
                when string.IsNullOrWhiteSpace(inquiry.BuyerEmail)
                    && string.IsNullOrWhiteSpace(inquiry.BuyerPhone):
                throw new ArgumentException("An email address or phone number is required for an offer.");
            case BuyerInquiryType.Agent when inquiry.AgentId is null:
                throw new ArgumentException("An agent inquiry requires an agent.");
        }
    }

    public async Task UpdateListingAsync(Listing listing)
    {
        listing.UpdatedAt = DateTime.UtcNow;

        _db.Listings.Update(listing);
        await _db.SaveChangesAsync();
        await _productEvents.RecordAsync(
            listing.UserId,
            ProductEventNames.ListingUpdated,
            listing.Id);

        _logger.LogInformation("Updated listing {ListingId}", listing.Id);
    }
}
