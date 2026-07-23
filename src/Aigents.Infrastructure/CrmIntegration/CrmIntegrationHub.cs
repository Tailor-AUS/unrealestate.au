using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.CrmIntegration;

/// <summary>
/// Central hub for managing CRM integrations. Handles adapter selection,
/// credential management, and provides unified access to all CRM operations.
/// </summary>
public interface ICrmIntegrationHub
{
    /// <summary>
    /// Get all available CRM adapters
    /// </summary>
    IReadOnlyList<CrmAdapterInfo> GetAvailableAdapters();

    /// <summary>
    /// Test connection to a specific CRM
    /// </summary>
    Task<CrmConnectionResult> TestConnectionAsync(string crmId, CrmCredentials credentials, CancellationToken ct = default);

    /// <summary>
    /// Import all contacts from agent's CRM
    /// </summary>
    Task<CrmImportResult> ImportContactsAsync(string agentId, string crmId, CrmCredentials credentials, CancellationToken ct = default);

    /// <summary>
    /// Search for a contact by phone across connected CRMs
    /// </summary>
    Task<CrmContact?> FindContactByPhoneAsync(string agentId, string phoneNumber, CancellationToken ct = default);

    /// <summary>
    /// Log a call as activity in the agent's CRM
    /// </summary>
    Task LogCallAsync(string agentId, CrmActivity activity, CancellationToken ct = default);

    /// <summary>
    /// Create a follow-up task in the agent's CRM
    /// </summary>
    Task CreateFollowUpAsync(string agentId, CrmTask task, CancellationToken ct = default);

    /// <summary>
    /// Get properties that match an address query
    /// </summary>
    Task<IReadOnlyList<CrmProperty>> SearchPropertiesAsync(string agentId, string addressQuery, CancellationToken ct = default);

    /// <summary>
    /// Get upcoming inspections for an agent
    /// </summary>
    Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(string agentId, CancellationToken ct = default);
}

public record CrmAdapterInfo(string CrmId, string DisplayName);

public record CrmImportResult
{
    public bool Success { get; init; }
    public int ContactsImported { get; init; }
    public int ContactsUpdated { get; init; }
    public int ContactsSkipped { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Implementation of the CRM Integration Hub
/// </summary>
public class CrmIntegrationHub : ICrmIntegrationHub
{
    private readonly Dictionary<string, ICrmAdapter> _adapters;
    private readonly IAgentCrmSettingsRepository _settingsRepo;
    private readonly ILogger<CrmIntegrationHub> _logger;

    public CrmIntegrationHub(
        IEnumerable<ICrmAdapter> adapters,
        IAgentCrmSettingsRepository settingsRepo,
        ILogger<CrmIntegrationHub> logger)
    {
        _adapters = adapters.ToDictionary(a => a.CrmId, StringComparer.OrdinalIgnoreCase);
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public IReadOnlyList<CrmAdapterInfo> GetAvailableAdapters()
    {
        return _adapters.Values
            .Select(a => new CrmAdapterInfo(a.CrmId, a.DisplayName))
            .ToList();
    }

    public async Task<CrmConnectionResult> TestConnectionAsync(string crmId, CrmCredentials credentials, CancellationToken ct = default)
    {
        if (!_adapters.TryGetValue(crmId, out var adapter))
        {
            return new CrmConnectionResult
            {
                Success = false,
                ErrorMessage = $"Unknown CRM: {crmId}"
            };
        }

        return await adapter.TestConnectionAsync(credentials, ct);
    }

    public async Task<CrmImportResult> ImportContactsAsync(string agentId, string crmId, CrmCredentials credentials, CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;

        if (!_adapters.TryGetValue(crmId, out var adapter))
        {
            return new CrmImportResult
            {
                Success = false,
                ErrorMessage = $"Unknown CRM: {crmId}"
            };
        }

        try
        {
            _logger.LogInformation("Starting CRM import for agent {AgentId} from {CrmId}", agentId, crmId);

            int imported = 0, updated = 0, skipped = 0;
            int page = 1;
            bool hasMore = true;

            while (hasMore && !ct.IsCancellationRequested)
            {
                var result = await adapter.GetContactsAsync(credentials, page, 100, null, ct);

                foreach (var contact in result.Items)
                {
                    // Here we would save to our database
                    // For now, just count
                    imported++;
                }

                hasMore = result.HasNextPage;
                page++;

                // Rate limiting - don't hammer the CRM
                await Task.Delay(100, ct);
            }

            // Save the connection for future use
            await _settingsRepo.SaveCrmConnectionAsync(agentId, crmId, credentials, ct);

            var duration = DateTimeOffset.UtcNow - startTime;
            _logger.LogInformation("CRM import complete for {AgentId}: {Imported} imported, {Updated} updated, {Skipped} skipped in {Duration}",
                agentId, imported, updated, skipped, duration);

            return new CrmImportResult
            {
                Success = true,
                ContactsImported = imported,
                ContactsUpdated = updated,
                ContactsSkipped = skipped,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CRM import failed for agent {AgentId} from {CrmId}", agentId, crmId);
            return new CrmImportResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = DateTimeOffset.UtcNow - startTime
            };
        }
    }

    public async Task<CrmContact?> FindContactByPhoneAsync(string agentId, string phoneNumber, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetCrmConnectionAsync(agentId, ct);
        if (settings == null)
        {
            _logger.LogWarning("No CRM connection found for agent {AgentId}", agentId);
            return null;
        }

        if (!_adapters.TryGetValue(settings.CrmId, out var adapter))
        {
            return null;
        }

        var contacts = await adapter.SearchContactsByPhoneAsync(settings.Credentials, phoneNumber, ct);
        return contacts.FirstOrDefault();
    }

    public async Task LogCallAsync(string agentId, CrmActivity activity, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetCrmConnectionAsync(agentId, ct);
        if (settings == null)
        {
            _logger.LogWarning("No CRM connection for agent {AgentId}, cannot log call", agentId);
            return;
        }

        if (!_adapters.TryGetValue(settings.CrmId, out var adapter))
        {
            return;
        }

        try
        {
            var activityId = await adapter.LogActivityAsync(settings.Credentials, activity, ct);
            _logger.LogInformation("Logged call to CRM {CrmId} for agent {AgentId}, activity ID: {ActivityId}",
                settings.CrmId, agentId, activityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log call to CRM for agent {AgentId}", agentId);
        }
    }

    public async Task CreateFollowUpAsync(string agentId, CrmTask task, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetCrmConnectionAsync(agentId, ct);
        if (settings == null)
        {
            _logger.LogWarning("No CRM connection for agent {AgentId}, cannot create task", agentId);
            return;
        }

        if (!_adapters.TryGetValue(settings.CrmId, out var adapter))
        {
            return;
        }

        try
        {
            var taskId = await adapter.CreateTaskAsync(settings.Credentials, task, ct);
            _logger.LogInformation("Created task in CRM {CrmId} for agent {AgentId}, task ID: {TaskId}",
                settings.CrmId, agentId, taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create task in CRM for agent {AgentId}", agentId);
        }
    }

    public async Task<IReadOnlyList<CrmProperty>> SearchPropertiesAsync(string agentId, string addressQuery, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetCrmConnectionAsync(agentId, ct);
        if (settings == null)
        {
            return [];
        }

        if (!_adapters.TryGetValue(settings.CrmId, out var adapter))
        {
            return [];
        }

        return await adapter.SearchPropertiesByAddressAsync(settings.Credentials, addressQuery, ct);
    }

    public async Task<IReadOnlyList<CrmInspection>> GetUpcomingInspectionsAsync(string agentId, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetCrmConnectionAsync(agentId, ct);
        if (settings == null)
        {
            return [];
        }

        if (!_adapters.TryGetValue(settings.CrmId, out var adapter))
        {
            return [];
        }

        return await adapter.GetUpcomingInspectionsAsync(settings.Credentials, settings.CrmAgentId, ct);
    }
}

/// <summary>
/// Repository for storing agent CRM connection settings
/// </summary>
public interface IAgentCrmSettingsRepository
{
    Task<AgentCrmSettings?> GetCrmConnectionAsync(string agentId, CancellationToken ct = default);
    Task SaveCrmConnectionAsync(string agentId, string crmId, CrmCredentials credentials, CancellationToken ct = default);
    Task DeleteCrmConnectionAsync(string agentId, CancellationToken ct = default);
}

public record AgentCrmSettings
{
    public required string AgentId { get; init; }
    public required string CrmId { get; init; }
    public string? CrmAgentId { get; init; }
    public required CrmCredentials Credentials { get; init; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
}
