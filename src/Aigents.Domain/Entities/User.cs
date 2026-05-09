using Microsoft.AspNetCore.Identity;

namespace Aigents.Domain.Entities;

/// <summary>
/// Application user — extends ASP.NET Core Identity with the lead/buyer/seller fields
/// the unrealestate.au domain cares about. Auth is sovereign-from-day-1: email +
/// password (with optional WebAuthn passkey), no third-party identity provider.
/// </summary>
public class User : IdentityUser<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    // Lead info
    public string? InterestedSuburb { get; set; }
    public AgentMode PreferredMode { get; set; } = AgentMode.Buy;
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public DateTime? HandedOffAt { get; set; }
    public string? AssignedAgentId { get; set; }

    // Login alert tracking
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? LastLoginUserAgent { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }

    // Navigation
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Fido2Credential> Fido2Credentials { get; set; } = new List<Fido2Credential>();

    public User()
    {
        Id = Guid.NewGuid();
    }
}

public enum AgentMode
{
    Buy,
    Sell
}

public enum LeadStatus
{
    New,
    Engaged,
    Qualified,
    HandedOff,
    Converted,
    Lost
}

/// <summary>
/// Stored WebAuthn / FIDO2 credential for passkey login. One user may register
/// multiple passkeys (e.g. phone + laptop).
/// </summary>
public class Fido2Credential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public byte[] UserHandle { get; set; } = Array.Empty<byte>();
    public uint SignatureCounter { get; set; }
    public string CredType { get; set; } = string.Empty;
    public Guid AaGuid { get; set; }
    public string? Nickname { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    public User? User { get; set; }
}
