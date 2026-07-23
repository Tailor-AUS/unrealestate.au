using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.PropertyData;

/// <summary>
/// Service for interacting with QLD Government MapsOnline API
/// Provides property reports including zoning, overlays, vegetation, and development constraints
/// API Docs: https://www.data.qld.gov.au/dataset/mapsonline-api
/// </summary>
public interface IMapsOnlineService
{
    /// <summary>
    /// Request a property report by Lot/Plan (e.g., "1,RP123456")
    /// Note: Reports are delivered via email as PDFs
    /// </summary>
    Task<MapsOnlineResponse> RequestReportAsync(string lotPlan, string reportType, string emailAddress, CancellationToken ct = default);

    /// <summary>
    /// Request a property report by coordinates (longitude, latitude)
    /// </summary>
    Task<MapsOnlineResponse> RequestReportByCoordinatesAsync(double longitude, double latitude, string reportType, string emailAddress, CancellationToken ct = default);

    /// <summary>
    /// Get available report types
    /// </summary>
    Task<List<MapsOnlineReportType>> GetReportTypesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get available feature types (ways to specify location)
    /// </summary>
    Task<List<MapsOnlineFeatureType>> GetFeatureTypesAsync(CancellationToken ct = default);
}

public class MapsOnlineService : IMapsOnlineService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MapsOnlineService> _logger;
    private const string BaseUrl = "https://mapsonline.information.qld.gov.au/service/environment/resource/MapsOnline/1/http/rest";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MapsOnlineService(HttpClient httpClient, ILogger<MapsOnlineService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MapsOnlineResponse> RequestReportAsync(
        string lotPlan,
        string reportType,
        string emailAddress,
        CancellationToken ct = default)
    {
        // Parse lot/plan - can be "1,RP123456" or "1/RP123456"
        var features = lotPlan.Replace("/", ",");

        var url = $"{BaseUrl}/request?reportType={reportType}&featureType=LotPlan&features={Uri.EscapeDataString(features)}&emailAddress={Uri.EscapeDataString(emailAddress)}";

        _logger.LogInformation("Requesting MapsOnline report: {ReportType} for {LotPlan}", reportType, lotPlan);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<MapsOnlineResponse>(url, JsonOptions, ct);

            if (response?.Success == true)
            {
                _logger.LogInformation("MapsOnline report requested successfully for {LotPlan}", lotPlan);
            }
            else
            {
                _logger.LogWarning("MapsOnline report request failed: {Message}", response?.Message);
            }

            return response ?? new MapsOnlineResponse { Success = false, Message = "No response received" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting MapsOnline report for {LotPlan}", lotPlan);
            return new MapsOnlineResponse
            {
                Success = false,
                Message = $"Request failed: {ex.Message}",
                MessageCode = "error"
            };
        }
    }

    public async Task<MapsOnlineResponse> RequestReportByCoordinatesAsync(
        double longitude,
        double latitude,
        string reportType,
        string emailAddress,
        CancellationToken ct = default)
    {
        // Coordinates in format: longitude,latitude (e.g., "153.0251,-27.4698" for Brisbane)
        var features = $"{longitude},{latitude}";

        var url = $"{BaseUrl}/request?reportType={reportType}&featureType=point&features={Uri.EscapeDataString(features)}&emailAddress={Uri.EscapeDataString(emailAddress)}";

        _logger.LogInformation("Requesting MapsOnline report: {ReportType} for coordinates {Lon},{Lat}", reportType, longitude, latitude);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<MapsOnlineResponse>(url, JsonOptions, ct);
            return response ?? new MapsOnlineResponse { Success = false, Message = "No response received" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting MapsOnline report for coordinates {Lon},{Lat}", longitude, latitude);
            return new MapsOnlineResponse
            {
                Success = false,
                Message = $"Request failed: {ex.Message}",
                MessageCode = "error"
            };
        }
    }

    public async Task<List<MapsOnlineReportType>> GetReportTypesAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/getReportTypes";
            var response = await _httpClient.GetFromJsonAsync<ReportTypesResponse>(url, JsonOptions, ct);
            return response?.Output ?? new List<MapsOnlineReportType>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching MapsOnline report types");
            return new List<MapsOnlineReportType>();
        }
    }

    public async Task<List<MapsOnlineFeatureType>> GetFeatureTypesAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/getFeatureTypes";
            var response = await _httpClient.GetFromJsonAsync<FeatureTypesResponse>(url, JsonOptions, ct);
            return response?.Output ?? new List<MapsOnlineFeatureType>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching MapsOnline feature types");
            return new List<MapsOnlineFeatureType>();
        }
    }

    // Response wrappers for the list endpoints
    private class ReportTypesResponse
    {
        public List<MapsOnlineReportType> Output { get; set; } = new();
        public bool Success { get; set; }
    }

    private class FeatureTypesResponse
    {
        public List<MapsOnlineFeatureType> Output { get; set; } = new();
        public bool Success { get; set; }
    }
}
