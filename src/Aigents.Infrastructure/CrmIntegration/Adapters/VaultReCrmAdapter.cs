using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.CrmIntegration.Adapters;

/// <summary>
/// VaultRE CRM Adapter - Integrates with MRI Vault CRM API
/// API Docs: https://developers.vaultre.com
/// Used by: Ray White, Harcourts, and other major networks
/// </summary>
public class VaultReCrmAdapter : ICrmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VaultReCrmAdapter> _logger;
    private const string DefaultBaseUrl = "https://api.vaultre.com.au/api/v1.3";

    public string CrmId => "vaultre";
    public string DisplayName => "VaultRE";

    public VaultReCrmAdapter(HttpClient httpClient, ILogger<VaultReCrmAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CrmConnectionResult> TestConnectionAsync(CrmCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/me", credentials);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new CrmConnectionResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }

            var user = await response.Content.ReadFromJsonAsync<VaultReUser>(cancellationToken: ct);

            return new CrmConnectionResult
            {
                Success = true,
                AgentName = user?.FullName,
                OfficeName = user?.OfficeName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VaultRE connection test failed");
            return new CrmConnectionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CrmPagedResult<CrmContact>> GetContactsAsync(
        CrmCredentials credentials,
        int page = 1,
        int pageSize = 100,
        DateTimeOffset? modifiedSince = null,
        CancellationToken ct = default)
    {
        var offset = (page - 1) * pageSize;
        var url = $"/contacts?limit={pageSize}&offset={offset}";
        if (modifiedSince.HasValue)
        {
            url += $"&modified_since={modifiedSince.Value:yyyy-MM-ddTHH:mm:ssZ}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var vaultResponse = await response.Content.ReadFromJsonAsync<VaultReListResponse<VaultReContact>>(cancellationToken: ct);

        return new CrmPagedResult<CrmContact>
        {
            Items = vaultResponse?.Items?.Select(MapToContact).ToList() ?? [],
            Page = page,
            PageSize = pageSize,
            TotalItems = vaultResponse?.TotalCount ?? 0
        };
    }

    public async Task<CrmContact?> GetContactByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/contacts/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var contact = await response.Content.ReadFromJsonAsync<VaultReContact>(cancellationToken: ct);
        return contact != null ? MapToContact(contact) : null;
    }

    public async Task<IReadOnlyList<CrmContact>> SearchContactsByPhoneAsync(CrmCredentials credentials, string phoneNumber, CancellationToken ct = default)
    {
        var normalized = NormalizePhone(phoneNumber);
        using var request = CreateRequest(HttpMethod.Get, $"/contacts/search?phone={Uri.EscapeDataString(normalized)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var vaultResponse = await response.Content.ReadFromJsonAsync<VaultReListResponse<VaultReContact>>(cancellationToken: ct);
        return vaultResponse?.Items?.Select(MapToContact).ToList() ?? [];
    }

    public async Task<CrmContact> CreateContactAsync(CrmCredentials credentials, CrmContact contact, CancellationToken ct = default)
    {
        var vaultContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Post, "/contacts", credentials);
        request.Content = JsonContent.Create(vaultContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<VaultReContact>(cancellationToken: ct);
        return MapToContact(created!);
    }

    public async Task<CrmContact> UpdateContactAsync(CrmCredentials credentials, string externalId, CrmContact contact, CancellationToken ct = default)
    {
        var vaultContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Put, $"/contacts/{externalId}", credentials);
        request.Content = JsonContent.Create(vaultContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<VaultReContact>(cancellationToken: ct);
        return MapToContact(updated!);
    }

    public async Task<CrmPagedResult<CrmProperty>> GetPropertiesAsync(CrmCredentials credentials, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var offset = (page - 1) * pageSize;
        using var request = CreateRequest(HttpMethod.Get, $"/listings?limit={pageSize}&offset={offset}&status=active", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var vaultResponse = await response.Content.ReadFromJsonAsync<VaultReListResponse<VaultReListing>>(cancellationToken: ct);

        return new CrmPagedResult<CrmProperty>
        {
            Items = vaultResponse?.Items?.Select(MapToProperty).ToList() ?? [],
            Page = page,
            PageSize = pageSize,
            TotalItems = vaultResponse?.TotalCount ?? 0
        };
    }

    public async Task<CrmProperty?> GetPropertyByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/listings/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var listing = await response.Content.ReadFromJsonAsync<VaultReListing>(cancellationToken: ct);
        return listing != null ? MapToProperty(listing) : null;
    }

    public async Task<IReadOnlyList<CrmProperty>> SearchPropertiesByAddressAsync(CrmCredentials credentials, string addressQuery, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/listings/search?address={Uri.EscapeDataString(addressQuery)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var vaultResponse = await response.Content.ReadFromJsonAsync<VaultReListResponse<VaultReListing>>(cancellationToken: ct);
        return vaultResponse?.Items?.Select(MapToProperty).ToList() ?? [];
    }

    public async Task<string> LogActivityAsync(CrmCredentials credentials, CrmActivity activity, CancellationToken ct = default)
    {
        var vaultNote = new VaultReNoteCreate
        {
            ContactId = activity.ContactId,
            ListingId = activity.PropertyId,
            Category = MapActivityCategory(activity.Type),
            Subject = activity.Subject,
            Body = activity.Description,
            ActivityDate = activity.Timestamp
        };

        using var request = CreateRequest(HttpMethod.Post, "/notes", credentials);
        request.Content = JsonContent.Create(vaultNote);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<VaultReNoteResponse>(cancellationToken: ct);
        return created?.Id?.ToString() ?? "";
    }

    public async Task<string> CreateTaskAsync(CrmCredentials credentials, CrmTask task, CancellationToken ct = default)
    {
        var vaultTask = new VaultReTaskCreate
        {
            ContactId = task.ContactId,
            ListingId = task.PropertyId,
            Subject = task.Subject,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = MapPriority(task.Priority),
            AssignedToUserId = task.AssignedToAgentId
        };

        using var request = CreateRequest(HttpMethod.Post, "/tasks", credentials);
        request.Content = JsonContent.Create(vaultTask);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<VaultReTaskResponse>(cancellationToken: ct);
        return created?.Id?.ToString() ?? "";
    }

    public async Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(CrmCredentials credentials, string? agentId = null, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var url = $"/inspections?from_date={today}&status=scheduled";
        if (!string.IsNullOrEmpty(agentId))
        {
            url += $"&agent_id={agentId}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var vaultResponse = await response.Content.ReadFromJsonAsync<VaultReListResponse<VaultReInspection>>(cancellationToken: ct);
        return vaultResponse?.Items?.Select(MapToInspection).ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, CrmCredentials credentials)
    {
        var baseUrl = credentials.BaseUrl ?? DefaultBaseUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        // VaultRE uses OAuth2 Bearer tokens
        if (!string.IsNullOrEmpty(credentials.AccessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        }

        request.Headers.Add("Accept", "application/json");

        return request;
    }

    private static string NormalizePhone(string phone)
    {
        return new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
    }

    private CrmContact MapToContact(VaultReContact v) => new()
    {
        ExternalId = v.Id?.ToString() ?? "",
        CrmSource = CrmId,
        FullName = v.DisplayName ?? $"{v.FirstName} {v.LastName}".Trim(),
        FirstName = v.FirstName,
        LastName = v.LastName,
        Email = v.Email,
        Phone = v.Phone,
        Mobile = v.Mobile,
        Classification = ParseClassification(v.ContactType),
        LeadSource = v.Source,
        LastContactDate = v.LastContactDate,
        CreatedAt = v.CreatedAt ?? DateTimeOffset.UtcNow,
        UpdatedAt = v.UpdatedAt ?? DateTimeOffset.UtcNow
    };

    private static VaultReContactCreate MapFromContact(CrmContact contact) => new()
    {
        FirstName = contact.FirstName ?? contact.FullName.Split(' ').FirstOrDefault(),
        LastName = contact.LastName ?? string.Join(' ', contact.FullName.Split(' ').Skip(1)),
        Email = contact.Email,
        Phone = contact.Phone,
        Mobile = contact.Mobile,
        ContactType = MapClassificationType(contact.Classification),
        Source = contact.LeadSource
    };

    private CrmProperty MapToProperty(VaultReListing v) => new()
    {
        ExternalId = v.Id?.ToString() ?? "",
        CrmSource = CrmId,
        Address = v.FullAddress ?? "",
        Suburb = v.Suburb,
        State = v.State,
        Postcode = v.Postcode,
        Type = ParsePropertyType(v.PropertyType),
        Status = ParseListingStatus(v.Status),
        PriceFrom = v.PriceFrom,
        PriceTo = v.PriceTo,
        PriceDisplay = v.PriceDisplay,
        Bedrooms = v.Bedrooms,
        Bathrooms = v.Bathrooms,
        CarSpaces = v.CarSpaces,
        AgentId = v.AgentId?.ToString(),
        ListedDate = v.ListedDate
    };

    private CrmInspection MapToInspection(VaultReInspection v) => new()
    {
        ExternalId = v.Id?.ToString() ?? "",
        PropertyId = v.ListingId?.ToString() ?? "",
        PropertyAddress = v.Address,
        StartTime = v.StartTime ?? DateTimeOffset.UtcNow,
        EndTime = v.EndTime ?? DateTimeOffset.UtcNow.AddMinutes(30),
        AgentId = v.AgentId?.ToString(),
        RsvpCount = v.AttendeeCount
    };

    private static ContactClassification ParseClassification(string? type) => type?.ToLowerInvariant() switch
    {
        "buyer" => ContactClassification.Buyer,
        "seller" or "vendor" => ContactClassification.Seller,
        "investor" => ContactClassification.Investor,
        "tenant" => ContactClassification.Tenant,
        "landlord" or "owner" => ContactClassification.Landlord,
        "agent" => ContactClassification.OtherAgent,
        _ => ContactClassification.Unknown
    };

    private static string MapClassificationType(ContactClassification c) => c switch
    {
        ContactClassification.Buyer => "buyer",
        ContactClassification.Seller => "seller",
        ContactClassification.Investor => "investor",
        ContactClassification.Tenant => "tenant",
        ContactClassification.Landlord => "landlord",
        ContactClassification.OtherAgent => "agent",
        _ => "other"
    };

    private static PropertyType ParsePropertyType(string? type) => type?.ToLowerInvariant() switch
    {
        "unit" => PropertyType.Unit,
        "apartment" => PropertyType.Apartment,
        "townhouse" => PropertyType.Townhouse,
        "land" => PropertyType.Land,
        "rural" => PropertyType.Rural,
        "commercial" => PropertyType.Commercial,
        _ => PropertyType.House
    };

    private static ListingStatus ParseListingStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "under_offer" or "under offer" or "conditional" => ListingStatus.UnderContract,
        "sold" or "settled" => ListingStatus.Sold,
        "withdrawn" => ListingStatus.Withdrawn,
        "off_market" => ListingStatus.OffMarket,
        _ => ListingStatus.Active
    };

    private static string MapActivityCategory(ActivityType type) => type switch
    {
        ActivityType.Call => "Phone Call",
        ActivityType.Email => "Email",
        ActivityType.SMS => "SMS",
        ActivityType.Inspection => "Inspection",
        ActivityType.Meeting => "Meeting",
        _ => "Note"
    };

    private static int MapPriority(TaskPriority p) => p switch
    {
        TaskPriority.Low => 1,
        TaskPriority.Normal => 2,
        TaskPriority.High => 3,
        TaskPriority.Urgent => 4,
        _ => 2
    };

    // ═══════════════════════════════════════════════════════════════
    // VAULTRE API RESPONSE MODELS
    // ═══════════════════════════════════════════════════════════════

    private record VaultReListResponse<T>
    {
        [JsonPropertyName("items")] public List<T>? Items { get; init; }
        [JsonPropertyName("total_count")] public int TotalCount { get; init; }
    }

    private record VaultReUser
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
        [JsonPropertyName("full_name")] public string? FullName { get; init; }
        [JsonPropertyName("office_name")] public string? OfficeName { get; init; }
    }

    private record VaultReContact
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
        [JsonPropertyName("first_name")] public string? FirstName { get; init; }
        [JsonPropertyName("last_name")] public string? LastName { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("mobile")] public string? Mobile { get; init; }
        [JsonPropertyName("contact_type")] public string? ContactType { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("last_contact_date")] public DateTimeOffset? LastContactDate { get; init; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    }

    private record VaultReContactCreate
    {
        [JsonPropertyName("first_name")] public string? FirstName { get; init; }
        [JsonPropertyName("last_name")] public string? LastName { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("mobile")] public string? Mobile { get; init; }
        [JsonPropertyName("contact_type")] public string? ContactType { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
    }

    private record VaultReListing
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
        [JsonPropertyName("full_address")] public string? FullAddress { get; init; }
        [JsonPropertyName("suburb")] public string? Suburb { get; init; }
        [JsonPropertyName("state")] public string? State { get; init; }
        [JsonPropertyName("postcode")] public string? Postcode { get; init; }
        [JsonPropertyName("property_type")] public string? PropertyType { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("price_from")] public decimal? PriceFrom { get; init; }
        [JsonPropertyName("price_to")] public decimal? PriceTo { get; init; }
        [JsonPropertyName("price_display")] public string? PriceDisplay { get; init; }
        [JsonPropertyName("bedrooms")] public int? Bedrooms { get; init; }
        [JsonPropertyName("bathrooms")] public int? Bathrooms { get; init; }
        [JsonPropertyName("car_spaces")] public int? CarSpaces { get; init; }
        [JsonPropertyName("agent_id")] public int? AgentId { get; init; }
        [JsonPropertyName("listed_date")] public DateTimeOffset? ListedDate { get; init; }
    }

    private record VaultReInspection
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
        [JsonPropertyName("listing_id")] public int? ListingId { get; init; }
        [JsonPropertyName("address")] public string? Address { get; init; }
        [JsonPropertyName("start_time")] public DateTimeOffset? StartTime { get; init; }
        [JsonPropertyName("end_time")] public DateTimeOffset? EndTime { get; init; }
        [JsonPropertyName("agent_id")] public int? AgentId { get; init; }
        [JsonPropertyName("attendee_count")] public int? AttendeeCount { get; init; }
    }

    private record VaultReNoteCreate
    {
        [JsonPropertyName("contact_id")] public string? ContactId { get; init; }
        [JsonPropertyName("listing_id")] public string? ListingId { get; init; }
        [JsonPropertyName("category")] public string? Category { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("activity_date")] public DateTimeOffset ActivityDate { get; init; }
    }

    private record VaultReNoteResponse
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
    }

    private record VaultReTaskCreate
    {
        [JsonPropertyName("contact_id")] public string? ContactId { get; init; }
        [JsonPropertyName("listing_id")] public string? ListingId { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("due_date")] public DateTimeOffset? DueDate { get; init; }
        [JsonPropertyName("priority")] public int Priority { get; init; }
        [JsonPropertyName("assigned_to_user_id")] public string? AssignedToUserId { get; init; }
    }

    private record VaultReTaskResponse
    {
        [JsonPropertyName("id")] public int? Id { get; init; }
    }
}
