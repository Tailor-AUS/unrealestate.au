using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.CrmIntegration.Adapters;

/// <summary>
/// AgentBox CRM Adapter - Integrates with Reapit's AgentBox API
/// API Docs: https://developer.reapit.cloud
/// </summary>
public class AgentBoxCrmAdapter : ICrmAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentBoxCrmAdapter> _logger;
    private const string DefaultBaseUrl = "https://platform.reapit.cloud";

    public string CrmId => "agentbox";
    public string DisplayName => "AgentBox";

    public AgentBoxCrmAdapter(HttpClient httpClient, ILogger<AgentBoxCrmAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CrmConnectionResult> TestConnectionAsync(CrmCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/negotiators/me", credentials);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new CrmConnectionResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }

            var negotiator = await response.Content.ReadFromJsonAsync<AgentBoxNegotiator>(cancellationToken: ct);

            return new CrmConnectionResult
            {
                Success = true,
                AgentName = negotiator?.Name,
                OfficeName = negotiator?.OfficeName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentBox connection test failed");
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
        var url = $"/contacts?pageNumber={page}&pageSize={pageSize}";
        if (modifiedSince.HasValue)
        {
            url += $"&modifiedFrom={modifiedSince.Value:O}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var abResponse = await response.Content.ReadFromJsonAsync<AgentBoxPagedResponse<AgentBoxContact>>(cancellationToken: ct);

        return new CrmPagedResult<CrmContact>
        {
            Items = abResponse?._embedded?.Select(MapToContact).ToList() ?? [],
            Page = page,
            PageSize = pageSize,
            TotalItems = abResponse?.TotalCount ?? 0
        };
    }

    public async Task<CrmContact?> GetContactByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/contacts/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var contact = await response.Content.ReadFromJsonAsync<AgentBoxContact>(cancellationToken: ct);
        return contact != null ? MapToContact(contact) : null;
    }

    public async Task<IReadOnlyList<CrmContact>> SearchContactsByPhoneAsync(CrmCredentials credentials, string phoneNumber, CancellationToken ct = default)
    {
        var normalizedPhone = NormalizePhone(phoneNumber);
        using var request = CreateRequest(HttpMethod.Get, $"/contacts?mobilePhone={Uri.EscapeDataString(normalizedPhone)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var abResponse = await response.Content.ReadFromJsonAsync<AgentBoxPagedResponse<AgentBoxContact>>(cancellationToken: ct);
        return abResponse?._embedded?.Select(MapToContact).ToList() ?? [];
    }

    public async Task<CrmContact> CreateContactAsync(CrmCredentials credentials, CrmContact contact, CancellationToken ct = default)
    {
        var abContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Post, "/contacts", credentials);
        request.Content = JsonContent.Create(abContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<AgentBoxContact>(cancellationToken: ct);
        return MapToContact(created!);
    }

    public async Task<CrmContact> UpdateContactAsync(CrmCredentials credentials, string externalId, CrmContact contact, CancellationToken ct = default)
    {
        var abContact = MapFromContact(contact);

        using var request = CreateRequest(HttpMethod.Patch, $"/contacts/{externalId}", credentials);
        request.Content = JsonContent.Create(abContact);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<AgentBoxContact>(cancellationToken: ct);
        return MapToContact(updated!);
    }

    public async Task<CrmPagedResult<CrmProperty>> GetPropertiesAsync(CrmCredentials credentials, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/properties?pageNumber={page}&pageSize={pageSize}&marketingMode=selling", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var abResponse = await response.Content.ReadFromJsonAsync<AgentBoxPagedResponse<AgentBoxProperty>>(cancellationToken: ct);

        return new CrmPagedResult<CrmProperty>
        {
            Items = abResponse?._embedded?.Select(MapToProperty).ToList() ?? [],
            Page = page,
            PageSize = pageSize,
            TotalItems = abResponse?.TotalCount ?? 0
        };
    }

    public async Task<CrmProperty?> GetPropertyByIdAsync(CrmCredentials credentials, string externalId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/properties/{externalId}", credentials);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var property = await response.Content.ReadFromJsonAsync<AgentBoxProperty>(cancellationToken: ct);
        return property != null ? MapToProperty(property) : null;
    }

    public async Task<IReadOnlyList<CrmProperty>> SearchPropertiesByAddressAsync(CrmCredentials credentials, string addressQuery, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/properties?address={Uri.EscapeDataString(addressQuery)}", credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var abResponse = await response.Content.ReadFromJsonAsync<AgentBoxPagedResponse<AgentBoxProperty>>(cancellationToken: ct);
        return abResponse?._embedded?.Select(MapToProperty).ToList() ?? [];
    }

    public async Task<string> LogActivityAsync(CrmCredentials credentials, CrmActivity activity, CancellationToken ct = default)
    {
        // AgentBox uses journal entries for activities
        var journalEntry = new AgentBoxJournalEntry
        {
            AssociatedType = activity.ContactId != null ? "contact" : "property",
            AssociatedId = activity.ContactId ?? activity.PropertyId ?? "",
            TypeId = MapActivityTypeId(activity.Type),
            Description = $"{activity.Subject}\n\n{activity.Description}",
            Timestamp = activity.Timestamp
        };

        using var request = CreateRequest(HttpMethod.Post, "/journalEntries", credentials);
        request.Content = JsonContent.Create(journalEntry);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<AgentBoxJournalEntryResponse>(cancellationToken: ct);
        return created?.Id ?? "";
    }

    public async Task<string> CreateTaskAsync(CrmCredentials credentials, CrmTask task, CancellationToken ct = default)
    {
        var abTask = new AgentBoxTaskCreate
        {
            ContactId = task.ContactId,
            PropertyId = task.PropertyId,
            Text = task.Subject,
            Notes = task.Description,
            Activate = task.DueDate,
            NegotiatorId = task.AssignedToAgentId
        };

        using var request = CreateRequest(HttpMethod.Post, "/tasks", credentials);
        request.Content = JsonContent.Create(abTask);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<AgentBoxTaskResponse>(cancellationToken: ct);
        return created?.Id ?? "";
    }

    public async Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(CrmCredentials credentials, string? agentId = null, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var url = $"/appointments?start={today}&type=viewing";
        if (!string.IsNullOrEmpty(agentId))
        {
            url += $"&negotiatorId={agentId}";
        }

        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var abResponse = await response.Content.ReadFromJsonAsync<AgentBoxPagedResponse<AgentBoxAppointment>>(cancellationToken: ct);
        return abResponse?._embedded?.Select(MapToInspection).ToList() ?? [];
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, CrmCredentials credentials)
    {
        var baseUrl = credentials.BaseUrl ?? DefaultBaseUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("api-version", "2021-08-01");

        return request;
    }

    private static string NormalizePhone(string phone)
    {
        return new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
    }

    private CrmContact MapToContact(AgentBoxContact ab) => new()
    {
        ExternalId = ab.Id ?? "",
        CrmSource = CrmId,
        FullName = $"{ab.Forename} {ab.Surname}".Trim(),
        FirstName = ab.Forename,
        LastName = ab.Surname,
        Email = ab.Email,
        Mobile = ab.MobilePhone,
        Phone = ab.HomePhone ?? ab.WorkPhone,
        Classification = ParseClassification(ab.MarketingConsent),
        LeadSource = ab.Source,
        CreatedAt = ab.Created ?? DateTimeOffset.UtcNow,
        UpdatedAt = ab.Modified ?? DateTimeOffset.UtcNow
    };

    private static AgentBoxContactCreate MapFromContact(CrmContact contact) => new()
    {
        Forename = contact.FirstName ?? contact.FullName.Split(' ').FirstOrDefault(),
        Surname = contact.LastName ?? string.Join(' ', contact.FullName.Split(' ').Skip(1)),
        Email = contact.Email,
        MobilePhone = contact.Mobile ?? contact.Phone,
        Source = contact.LeadSource
    };

    private CrmProperty MapToProperty(AgentBoxProperty ab) => new()
    {
        ExternalId = ab.Id ?? "",
        CrmSource = CrmId,
        Address = ab.Address?.BuildingNumber != null
            ? $"{ab.Address.BuildingNumber} {ab.Address.Line1}".Trim()
            : ab.Address?.Line1 ?? "",
        Suburb = ab.Address?.Line3,
        State = ab.Address?.Line4,
        Postcode = ab.Address?.Postcode,
        Type = ParsePropertyType(ab.Type),
        Status = ParseListingStatus(ab.Selling?.Status),
        PriceFrom = ab.Selling?.Price,
        Bedrooms = ab.Bedrooms,
        Bathrooms = ab.Bathrooms
    };

    private CrmInspection MapToInspection(AgentBoxAppointment ab) => new()
    {
        ExternalId = ab.Id ?? "",
        PropertyId = ab.PropertyId ?? "",
        StartTime = ab.Start ?? DateTimeOffset.UtcNow,
        EndTime = ab.End ?? DateTimeOffset.UtcNow.AddMinutes(30),
        AgentId = ab.NegotiatorIds?.FirstOrDefault()
    };

    private static ContactClassification ParseClassification(string? type) => type?.ToLowerInvariant() switch
    {
        "buying" => ContactClassification.Buyer,
        "selling" => ContactClassification.Seller,
        _ => ContactClassification.Unknown
    };

    private static PropertyType ParsePropertyType(string? type) => type?.ToLowerInvariant() switch
    {
        "flat" or "apartment" => PropertyType.Apartment,
        "house" => PropertyType.House,
        "land" => PropertyType.Land,
        _ => PropertyType.House
    };

    private static ListingStatus ParseListingStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "underOffer" => ListingStatus.UnderContract,
        "sold" or "completed" => ListingStatus.Sold,
        "withdrawn" => ListingStatus.Withdrawn,
        _ => ListingStatus.Active
    };

    private static string MapActivityTypeId(ActivityType type) => type switch
    {
        ActivityType.Call => "telephoneCall",
        ActivityType.Email => "email",
        ActivityType.SMS => "sms",
        ActivityType.Inspection => "viewing",
        ActivityType.Meeting => "meeting",
        _ => "note"
    };

    // ═══════════════════════════════════════════════════════════════
    // AGENTBOX API RESPONSE MODELS
    // ═══════════════════════════════════════════════════════════════

    private record AgentBoxPagedResponse<T>
    {
        [JsonPropertyName("_embedded")] public List<T>? _embedded { get; init; }
        [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    }

    private record AgentBoxNegotiator
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("officeName")] public string? OfficeName { get; init; }
    }

    private record AgentBoxContact
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("forename")] public string? Forename { get; init; }
        [JsonPropertyName("surname")] public string? Surname { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("mobilePhone")] public string? MobilePhone { get; init; }
        [JsonPropertyName("homePhone")] public string? HomePhone { get; init; }
        [JsonPropertyName("workPhone")] public string? WorkPhone { get; init; }
        [JsonPropertyName("marketingConsent")] public string? MarketingConsent { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("created")] public DateTimeOffset? Created { get; init; }
        [JsonPropertyName("modified")] public DateTimeOffset? Modified { get; init; }
    }

    private record AgentBoxContactCreate
    {
        [JsonPropertyName("forename")] public string? Forename { get; init; }
        [JsonPropertyName("surname")] public string? Surname { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("mobilePhone")] public string? MobilePhone { get; init; }
        [JsonPropertyName("source")] public string? Source { get; init; }
    }

    private record AgentBoxProperty
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("address")] public AgentBoxAddress? Address { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("bedrooms")] public int? Bedrooms { get; init; }
        [JsonPropertyName("bathrooms")] public int? Bathrooms { get; init; }
        [JsonPropertyName("selling")] public AgentBoxSelling? Selling { get; init; }
    }

    private record AgentBoxAddress
    {
        [JsonPropertyName("buildingNumber")] public string? BuildingNumber { get; init; }
        [JsonPropertyName("line1")] public string? Line1 { get; init; }
        [JsonPropertyName("line3")] public string? Line3 { get; init; }
        [JsonPropertyName("line4")] public string? Line4 { get; init; }
        [JsonPropertyName("postcode")] public string? Postcode { get; init; }
    }

    private record AgentBoxSelling
    {
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("price")] public decimal? Price { get; init; }
    }

    private record AgentBoxAppointment
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("propertyId")] public string? PropertyId { get; init; }
        [JsonPropertyName("start")] public DateTimeOffset? Start { get; init; }
        [JsonPropertyName("end")] public DateTimeOffset? End { get; init; }
        [JsonPropertyName("negotiatorIds")] public List<string>? NegotiatorIds { get; init; }
    }

    private record AgentBoxJournalEntry
    {
        [JsonPropertyName("associatedType")] public string? AssociatedType { get; init; }
        [JsonPropertyName("associatedId")] public string? AssociatedId { get; init; }
        [JsonPropertyName("typeId")] public string? TypeId { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    }

    private record AgentBoxJournalEntryResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }

    private record AgentBoxTaskCreate
    {
        [JsonPropertyName("contactId")] public string? ContactId { get; init; }
        [JsonPropertyName("propertyId")] public string? PropertyId { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("notes")] public string? Notes { get; init; }
        [JsonPropertyName("activate")] public DateTimeOffset? Activate { get; init; }
        [JsonPropertyName("negotiatorId")] public string? NegotiatorId { get; init; }
    }

    private record AgentBoxTaskResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }
}
