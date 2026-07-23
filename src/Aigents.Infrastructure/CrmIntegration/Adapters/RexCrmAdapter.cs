using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.CrmIntegration.Adapters;

/// <summary>
/// Rex CRM Adapter - Integrates with rexsoftware.com API
/// API Docs: https://api.rexsoftware.com/docs
/// </summary>
public class RexCrmAdapter : ICrmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RexCrmAdapter> _logger;
    private const string DefaultBaseUrl = "https://api.rexsoftware.com/v1";

    public string CrmId => "rex";
    public string DisplayName => "Rex";

    public RexCrmAdapter(HttpClient httpClient, ILogger<RexCrmAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CrmConnectionResult> TestConnectionAsync(CrmCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/account", credentials);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new CrmConnectionResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }

            var account = await response.Content.ReadFromJsonAsync<RexAccountResponse>(cancellationToken: ct);

            return new CrmConnectionResult
            {
                Success = true,
                AgentName = account?.Name,
                OfficeName = account?.OfficeName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rex connection test failed");
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
        var url = $"/contacts?page={page}&per_page={pageSize}";
        if (modifiedSince.HasValue)
        {
            url += $"&modified_since={modifiedSince.Value:O}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var rexResponse = await response.Content.ReadFromJsonAsync<RexPagedResponse<RexContact>>(cancellationToken: ct);

        return new CrmPagedResult<CrmContact>
        {
            Items = rexResponse?.Data?.Select(MapToContact).ToList() ?? [],
            Page = rexResponse?.Meta?.CurrentPage ?? page,
            PageSize = pageSize,
            TotalItems = rexResponse?.Meta?.Total ?? 0
        };
    }

    public async Task<CrmContact?> GetContactByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/contacts/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var rexContact = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexContact>>(cancellationToken: ct);
        return rexContact?.Data != null ? MapToContact(rexContact.Data) : null;
    }

    public async Task<IReadOnlyList<CrmContact>> SearchContactsByPhoneAsync(CrmCredentials credentials, string phoneNumber, CancellationToken ct = default)
    {
        var normalizedPhone = NormalizePhone(phoneNumber);
        using var request = CreateRequest(HttpMethod.Get, $"/contacts?phone={Uri.EscapeDataString(normalizedPhone)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var rexResponse = await response.Content.ReadFromJsonAsync<RexPagedResponse<RexContact>>(cancellationToken: ct);
        return rexResponse?.Data?.Select(MapToContact).ToList() ?? [];
    }

    public async Task<CrmContact> CreateContactAsync(CrmCredentials credentials, CrmContact contact, CancellationToken ct = default)
    {
        var rexContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Post, "/contacts", credentials);
        request.Content = JsonContent.Create(rexContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexContact>>(cancellationToken: ct);
        return MapToContact(created!.Data!);
    }

    public async Task<CrmContact> UpdateContactAsync(CrmCredentials credentials, string externalId, CrmContact contact, CancellationToken ct = default)
    {
        var rexContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Put, $"/contacts/{externalId}", credentials);
        request.Content = JsonContent.Create(rexContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexContact>>(cancellationToken: ct);
        return MapToContact(updated!.Data!);
    }

    public async Task<CrmPagedResult<CrmProperty>> GetPropertiesAsync(CrmCredentials credentials, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/listings?page={page}&per_page={pageSize}&status=active", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var rexResponse = await response.Content.ReadFromJsonAsync<RexPagedResponse<RexListing>>(cancellationToken: ct);

        return new CrmPagedResult<CrmProperty>
        {
            Items = rexResponse?.Data?.Select(MapToProperty).ToList() ?? [],
            Page = rexResponse?.Meta?.CurrentPage ?? page,
            PageSize = pageSize,
            TotalItems = rexResponse?.Meta?.Total ?? 0
        };
    }

    public async Task<CrmProperty?> GetPropertyByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/listings/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var listing = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexListing>>(cancellationToken: ct);
        return listing?.Data != null ? MapToProperty(listing.Data) : null;
    }

    public async Task<IReadOnlyList<CrmProperty>> SearchPropertiesByAddressAsync(CrmCredentials credentials, string addressQuery, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/listings?address={Uri.EscapeDataString(addressQuery)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var rexResponse = await response.Content.ReadFromJsonAsync<RexPagedResponse<RexListing>>(cancellationToken: ct);
        return rexResponse?.Data?.Select(MapToProperty).ToList() ?? [];
    }

    public async Task<string> LogActivityAsync(CrmCredentials credentials, CrmActivity activity, CancellationToken ct = default)
    {
        var rexActivity = new RexActivityCreate
        {
            ContactId = activity.ContactId,
            ListingId = activity.PropertyId,
            Type = MapActivityType(activity.Type),
            Subject = activity.Subject,
            Notes = activity.Description,
            Timestamp = activity.Timestamp,
            DurationMinutes = activity.DurationSeconds.HasValue ? activity.DurationSeconds.Value / 60 : null
        };

        using var request = CreateRequest(HttpMethod.Post, "/activities", credentials);
        request.Content = JsonContent.Create(rexActivity);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexActivity>>(cancellationToken: ct);
        return created?.Data?.Id ?? "";
    }

    public async Task<string> CreateTaskAsync(CrmCredentials credentials, CrmTask task, CancellationToken ct = default)
    {
        var rexTask = new RexTaskCreate
        {
            ContactId = task.ContactId,
            ListingId = task.PropertyId,
            Subject = task.Subject,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = MapPriority(task.Priority),
            AssignedTo = task.AssignedToAgentId
        };

        using var request = CreateRequest(HttpMethod.Post, "/tasks", credentials);
        request.Content = JsonContent.Create(rexTask);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<RexSingleResponse<RexTaskResponse>>(cancellationToken: ct);
        return created?.Data?.Id ?? "";
    }

    public async Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(CrmCredentials credentials, string? agentId = null, CancellationToken ct = default)
    {
        var url = "/inspections?upcoming=true";
        if (!string.IsNullOrEmpty(agentId))
        {
            url += $"&agent_id={agentId}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var rexResponse = await response.Content.ReadFromJsonAsync<RexPagedResponse<RexInspection>>(cancellationToken: ct);
        return rexResponse?.Data?.Select(MapToInspection).ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, CrmCredentials credentials)
    {
        var baseUrl = credentials.BaseUrl ?? DefaultBaseUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        if (!string.IsNullOrEmpty(credentials.AccessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        }
        else if (!string.IsNullOrEmpty(credentials.ApiKey))
        {
            request.Headers.Add("X-Api-Key", credentials.ApiKey);
        }

        return request;
    }

    private static string NormalizePhone(string phone)
    {
        return new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
    }

    private CrmContact MapToContact(RexContact rex) => new()
    {
        ExternalId = rex.Id ?? "",
        CrmSource = CrmId,
        FullName = $"{rex.FirstName} {rex.LastName}".Trim(),
        FirstName = rex.FirstName,
        LastName = rex.LastName,
        Email = rex.Email,
        Phone = rex.Phone,
        Mobile = rex.Mobile,
        Classification = ParseClassification(rex.Type),
        LeadSource = rex.Source,
        LastContactDate = rex.LastContactedAt,
        CreatedAt = rex.CreatedAt ?? DateTimeOffset.UtcNow,
        UpdatedAt = rex.UpdatedAt ?? DateTimeOffset.UtcNow
    };

    private static RexContactCreate MapFromContact(CrmContact contact) => new()
    {
        FirstName = contact.FirstName ?? contact.FullName.Split(' ').FirstOrDefault(),
        LastName = contact.LastName ?? string.Join(' ', contact.FullName.Split(' ').Skip(1)),
        Email = contact.Email,
        Phone = contact.Phone,
        Mobile = contact.Mobile,
        Type = MapClassificationType(contact.Classification),
        Source = contact.LeadSource
    };

    private CrmProperty MapToProperty(RexListing rex) => new()
    {
        ExternalId = rex.Id ?? "",
        CrmSource = CrmId,
        Address = rex.Address ?? "",
        Suburb = rex.Suburb,
        State = rex.State,
        Postcode = rex.Postcode,
        Type = ParsePropertyType(rex.PropertyType),
        Status = ParseListingStatus(rex.Status),
        PriceFrom = rex.PriceFrom,
        PriceTo = rex.PriceTo,
        PriceDisplay = rex.PriceDisplay,
        Bedrooms = rex.Bedrooms,
        Bathrooms = rex.Bathrooms,
        CarSpaces = rex.CarSpaces,
        AgentId = rex.AgentId,
        ListedDate = rex.ListedAt
    };

    private CrmInspection MapToInspection(RexInspection rex) => new()
    {
        ExternalId = rex.Id ?? "",
        PropertyId = rex.ListingId ?? "",
        PropertyAddress = rex.Address,
        StartTime = rex.StartTime ?? DateTimeOffset.UtcNow,
        EndTime = rex.EndTime ?? DateTimeOffset.UtcNow.AddMinutes(30),
        AgentId = rex.AgentId,
        RsvpCount = rex.RsvpCount
    };

    private static ContactClassification ParseClassification(string? type) => type?.ToLowerInvariant() switch
    {
        "buyer" => ContactClassification.Buyer,
        "seller" or "vendor" => ContactClassification.Seller,
        "investor" => ContactClassification.Investor,
        "tenant" => ContactClassification.Tenant,
        "landlord" => ContactClassification.Landlord,
        "agent" => ContactClassification.OtherAgent,
        _ => ContactClassification.Unknown
    };

    private static string MapClassificationType(ContactClassification classification) => classification switch
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
        "under_contract" or "under contract" => ListingStatus.UnderContract,
        "sold" => ListingStatus.Sold,
        "withdrawn" => ListingStatus.Withdrawn,
        "off_market" => ListingStatus.OffMarket,
        _ => ListingStatus.Active
    };

    private static string MapActivityType(ActivityType type) => type switch
    {
        ActivityType.Call => "call",
        ActivityType.Email => "email",
        ActivityType.SMS => "sms",
        ActivityType.Inspection => "inspection",
        ActivityType.Meeting => "meeting",
        ActivityType.Task => "task",
        _ => "note"
    };

    private static string MapPriority(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "low",
        TaskPriority.High => "high",
        TaskPriority.Urgent => "urgent",
        _ => "normal"
    };

    // ═══════════════════════════════════════════════════════════════
    // REX API RESPONSE MODELS
    // ═══════════════════════════════════════════════════════════════

    private record RexPagedResponse<T>
    {
        [JsonPropertyName("data")] public List<T>? Data { get; init; }
        [JsonPropertyName("meta")] public RexMeta? Meta { get; init; }
    }

    private record RexSingleResponse<T>
    {
        [JsonPropertyName("data")] public T? Data { get; init; }
    }

    private record RexMeta
    {
        [JsonPropertyName("current_page")] public int CurrentPage { get; init; }
        [JsonPropertyName("total")] public int Total { get; init; }
    }

    private record RexAccountResponse
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("office_name")] public string? OfficeName { get; init; }
    }

    private record RexContact
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("first_name")] public string? FirstName { get; init; }
        [JsonPropertyName("last_name")] public string? LastName { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("mobile")] public string? Mobile { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("last_contacted_at")] public DateTimeOffset? LastContactedAt { get; init; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    }

    private record RexContactCreate
    {
        [JsonPropertyName("first_name")] public string? FirstName { get; init; }
        [JsonPropertyName("last_name")] public string? LastName { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("mobile")] public string? Mobile { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
    }

    private record RexListing
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("address")] public string? Address { get; init; }
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
        [JsonPropertyName("agent_id")] public string? AgentId { get; init; }
        [JsonPropertyName("listed_at")] public DateTimeOffset? ListedAt { get; init; }
    }

    private record RexInspection
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("listing_id")] public string? ListingId { get; init; }
        [JsonPropertyName("address")] public string? Address { get; init; }
        [JsonPropertyName("start_time")] public DateTimeOffset? StartTime { get; init; }
        [JsonPropertyName("end_time")] public DateTimeOffset? EndTime { get; init; }
        [JsonPropertyName("agent_id")] public string? AgentId { get; init; }
        [JsonPropertyName("rsvp_count")] public int? RsvpCount { get; init; }
    }

    private record RexActivity
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }

    private record RexActivityCreate
    {
        [JsonPropertyName("contact_id")] public string? ContactId { get; init; }
        [JsonPropertyName("listing_id")] public string? ListingId { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("notes")] public string? Notes { get; init; }
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
        [JsonPropertyName("duration_minutes")] public int? DurationMinutes { get; init; }
    }

    private record RexTaskResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }

    private record RexTaskCreate
    {
        [JsonPropertyName("contact_id")] public string? ContactId { get; init; }
        [JsonPropertyName("listing_id")] public string? ListingId { get; init; }
        [JsonPropertyName("subject")] public string? Subject { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("due_date")] public DateTimeOffset? DueDate { get; init; }
        [JsonPropertyName("priority")] public string? Priority { get; init; }
        [JsonPropertyName("assigned_to")] public string? AssignedTo { get; init; }
    }
}
