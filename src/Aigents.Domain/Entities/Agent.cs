namespace Aigents.Domain.Entities;

/// <summary>
/// Represents a real estate agent who can receive off-market listings
/// </summary>
public class Agent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Basic Info
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    // Agency
    public string AgencyName { get; set; } = string.Empty;
    public string? AgencyLogo { get; set; }
    public string? LicenseNumber { get; set; }

    // Coverage Area
    public string Suburbs { get; set; } = "[]"; // JSON array of suburb names
    public string Postcodes { get; set; } = "[]"; // JSON array of postcodes

    // Preferences
    public bool AcceptsOffMarket { get; set; } = true;
    public decimal? MinPropertyValue { get; set; }
    public decimal? MaxPropertyValue { get; set; }
    public string PreferredPropertyTypes { get; set; } = "[\"House\",\"Unit\",\"Townhouse\"]";

    // Stats
    public int ListingsReceived { get; set; }
    public int ListingsConverted { get; set; } // Successfully sold
    public decimal TotalCommissionEarned { get; set; }

    // Status
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }

    // Navigation
    public ICollection<ListingInquiry> Inquiries { get; set; } = new List<ListingInquiry>();
}

/// <summary>
/// Tracks which agents received which listings
/// </summary>
public class ListingDistribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid AgentId { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool EmailSent { get; set; }
    public bool SmsSent { get; set; }
    public DateTime? ViewedAt { get; set; }

    // Navigation
    public Listing Listing { get; set; } = null!;
    public Agent Agent { get; set; } = null!;
}
