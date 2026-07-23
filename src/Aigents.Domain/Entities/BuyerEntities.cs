using System.ComponentModel.DataAnnotations.Schema;

namespace Aigents.Domain.Entities;

/// <summary>
/// A property saved by a buyer (watchlist/shortlist)
/// </summary>
public class UserSavedProperty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Property Reference (External)
    public string PropertyId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "Domain", "CoreLogic"
    public string Address { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? PriceEstimate { get; set; }

    // User Context
    public string? Notes { get; set; }
    public SavedPropertyStatus Status { get; set; } = SavedPropertyStatus.Saved;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public User? User { get; set; }
}

public enum SavedPropertyStatus
{
    Saved,
    ContactedAgent,
    Inspected,
    Offered,
    Purchased,
    Archived
}

/// <summary>
/// A saved search configuration for alerts
/// </summary>
public class UserSavedSearch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty; // "3 Bed Houses in Manly"

    // Filter Criteria (JSON stored or explicit columns)
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public string? Postcode { get; set; }
    public int? MinBedrooms { get; set; }
    public int? MinBathrooms { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    public bool AlertsEnabled { get; set; } = true;
    public AlertFrequency Frequency { get; set; } = AlertFrequency.Instant;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public User? User { get; set; }
}

public enum AlertFrequency
{
    Instant,
    Daily,
    Weekly
}
