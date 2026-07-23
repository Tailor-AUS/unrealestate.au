namespace Aigents.Infrastructure.CrmIntegration;

/// <summary>
/// Interface for CRM integrations. Each CRM adapter (Rex, AgentBox, VaultRE, etc.)
/// implements this interface to provide a consistent API for the Aigents platform.
/// </summary>
public interface ICrmAdapter
{
    /// <summary>
    /// Unique identifier for this CRM (e.g., "rex", "agentbox", "vaultre")
    /// </summary>
    string CrmId { get; }

    /// <summary>
    /// Display name for this CRM
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Test the connection to the CRM
    /// </summary>
    Task<CrmConnectionResult> TestConnectionAsync(CrmCredentials credentials, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // CONTACTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieve all contacts from the CRM (paginated)
    /// </summary>
    Task<CrmPagedResult<CrmContact>> GetContactsAsync(
        CrmCredentials credentials,
        int page = 1,
        int pageSize = 100,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single contact by external ID
    /// </summary>
    Task<CrmContact?> GetContactByIdAsync(
        CrmCredentials credentials,
        string externalId,
        CancellationToken ct = default);

    /// <summary>
    /// Search contacts by phone number
    /// </summary>
    Task<IReadOnlyList<CrmContact>> SearchContactsByPhoneAsync(
        CrmCredentials credentials,
        string phoneNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Create a new contact in the CRM
    /// </summary>
    Task<CrmContact> CreateContactAsync(
        CrmCredentials credentials,
        CrmContact contact,
        CancellationToken ct = default);

    /// <summary>
    /// Update an existing contact
    /// </summary>
    Task<CrmContact> UpdateContactAsync(
        CrmCredentials credentials,
        string externalId,
        CrmContact contact,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Retrieve all active listings
    /// </summary>
    Task<CrmPagedResult<CrmProperty>> GetPropertiesAsync(
        CrmCredentials credentials,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single property by external ID
    /// </summary>
    Task<CrmProperty?> GetPropertyByIdAsync(
        CrmCredentials credentials,
        string externalId,
        CancellationToken ct = default);

    /// <summary>
    /// Search properties by address
    /// </summary>
    Task<IReadOnlyList<CrmProperty>> SearchPropertiesByAddressAsync(
        CrmCredentials credentials,
        string addressQuery,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // ACTIVITIES & TASKS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Log an activity (call, note, etc.) to the CRM
    /// </summary>
    Task<string> LogActivityAsync(
        CrmCredentials credentials,
        CrmActivity activity,
        CancellationToken ct = default);

    /// <summary>
    /// Create a task/follow-up in the CRM
    /// </summary>
    Task<string> CreateTaskAsync(
        CrmCredentials credentials,
        CrmTask task,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // INSPECTIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get upcoming inspections for an agent
    /// </summary>
    Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(
        CrmCredentials credentials,
        string? agentId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Credentials for connecting to a CRM
/// </summary>
public record CrmCredentials
{
    public required string AgentId { get; init; }
    public string? ApiKey { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? TokenExpiry { get; init; }
    public string? BaseUrl { get; init; }
    public Dictionary<string, string> AdditionalSettings { get; init; } = new();
}

/// <summary>
/// Result of a connection test
/// </summary>
public record CrmConnectionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AgentName { get; init; }
    public string? OfficeName { get; init; }
}

/// <summary>
/// Paginated result from CRM queries
/// </summary>
public record CrmPagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}
