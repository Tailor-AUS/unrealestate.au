using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Aigents.Infrastructure.CrmIntegration;

/// <summary>
/// In-memory implementation of CRM settings repository.
/// Uses Redis for distributed caching in production, or local memory for dev.
/// </summary>
public class DistributedCrmSettingsRepository : IAgentCrmSettingsRepository
{
    private readonly IDistributedCache _cache;
    private const string KeyPrefix = "crm-settings:";

    public DistributedCrmSettingsRepository(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<AgentCrmSettings?> GetCrmConnectionAsync(string agentId, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{agentId}";
        var data = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(data))
            return null;

        return JsonSerializer.Deserialize<AgentCrmSettings>(data);
    }

    public async Task SaveCrmConnectionAsync(string agentId, string crmId, CrmCredentials credentials, CancellationToken ct = default)
    {
        var settings = new AgentCrmSettings
        {
            AgentId = agentId,
            CrmId = crmId,
            Credentials = credentials,
            ConnectedAt = DateTimeOffset.UtcNow
        };

        var key = $"{KeyPrefix}{agentId}";
        var data = JsonSerializer.Serialize(settings);

        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromDays(30)
        };

        await _cache.SetStringAsync(key, data, options, ct);
    }

    public async Task DeleteCrmConnectionAsync(string agentId, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{agentId}";
        await _cache.RemoveAsync(key, ct);
    }
}

/// <summary>
/// Simple in-memory implementation for development/testing
/// </summary>
public class InMemoryCrmSettingsRepository : IAgentCrmSettingsRepository
{
    private readonly Dictionary<string, AgentCrmSettings> _settings = new();

    public Task<AgentCrmSettings?> GetCrmConnectionAsync(string agentId, CancellationToken ct = default)
    {
        _settings.TryGetValue(agentId, out var settings);
        return Task.FromResult(settings);
    }

    public Task SaveCrmConnectionAsync(string agentId, string crmId, CrmCredentials credentials, CancellationToken ct = default)
    {
        _settings[agentId] = new AgentCrmSettings
        {
            AgentId = agentId,
            CrmId = crmId,
            Credentials = credentials,
            ConnectedAt = DateTimeOffset.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task DeleteCrmConnectionAsync(string agentId, CancellationToken ct = default)
    {
        _settings.Remove(agentId);
        return Task.CompletedTask;
    }
}
