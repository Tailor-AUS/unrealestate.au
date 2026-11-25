using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aigents.Domain.Matching;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.Import;

public sealed record ImportedListingDraft(
    bool Succeeded,
    bool FetchBlocked,
    string SourceUrl,
    string SourceHost,
    string? Address,
    string? Suburb,
    string? State,
    string? Postcode,
    string PropertyType,
    int? Bedrooms,
    int? Bathrooms,
    int? CarSpaces,
    int? LandSize,
    decimal? AskingPrice,
    string? PriceDisplay,
    string? Headline,
    string? Description,
    string? HeroImageUrl,
    string? AgencyName,
    string? Notice);

public interface IListingLinkImporter
{
    Task<ImportedListingDraft> ImportFromUrlAsync(string rawUrl, CancellationToken ct = default);
}

/// <summary>
/// SSRF-hardened, 1-link listing auto-importer for Australian real estate agents.
/// Agents paste their existing listing URL (agency site or portal page they own/control);
/// we safely read publicly exposed structured metadata (JSON-LD, Open Graph, meta tags)
/// and AU URL/text patterns — never bypassing anti-bot controls, never fetching private IPs,
/// never allowing non-default ports, HTTPS->HTTP downgrades, or Slowloris hangs.
/// </summary>
public sealed partial class ListingLinkImporter : IListingLinkImporter
{
    public const string HttpClientName = "ListingLinkImporter";
    public const int MaxResponseBytes = 512 * 1024;
    public const int MaxRedirects = 3;
    public static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly HashSet<string> ValidAuStates =
        new(ManualAddressResolution.ValidAustralianStates, StringComparer.OrdinalIgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ListingLinkImporter> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _dnsResolver;

    public ListingLinkImporter(
        IHttpClientFactory httpClientFactory,
        ILogger<ListingLinkImporter> logger,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dnsResolver = dnsResolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
    }

    public static bool ValidatePublicHttpUri(string? rawUrl, out Uri? uri, out string? error, bool requireHttps = false)
    {
        uri = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            error = "Paste a listing URL (https://...) to auto-import.";
            return false;
        }

        var trimmed = rawUrl.Trim();
        // Only prepend https:// when the input does not already contain a scheme (so ftp://, file://, javascript: are rejected).
        if (!trimmed.Contains("://", StringComparison.Ordinal) && !trimmed.Contains(':', StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            error = "That doesn't look like a valid web link.";
            return false;
        }

        if (requireHttps)
        {
            if (parsed.Scheme != Uri.UriSchemeHttps)
            {
                error = "Only https:// URLs are supported.";
                return false;
            }
        }
        else if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            error = "Only http:// and https:// listing links are supported.";
            return false;
        }

        if (!parsed.IsDefaultPort)
        {
            error = "Custom TCP ports are blocked for safety.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "Listing links cannot contain embedded credentials or an empty host.";
            return false;
        }

        var host = parsed.Host.TrimEnd('.').ToLowerInvariant();
        if (host is "localhost" or "localhost.localdomain"
            || host.EndsWith(".local", StringComparison.Ordinal)
            || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.EndsWith(".localhost", StringComparison.Ordinal)
            || !host.Contains('.'))
        {
            error = "Private or local network links are blocked.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ipLiteral) && IsPrivateOrReservedAddress(ipLiteral))
        {
            error = "Private or reserved IP addresses are blocked.";
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsPrivateOrReservedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
            || ip.Equals(IPAddress.Broadcast) || ip.Equals(IPAddress.None))
            return true;

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            var b0 = bytes[0];
            var b1 = bytes[1];
            return b0 == 0                                          // 0.0.0.0/8
                || b0 == 10                                         // 10.0.0.0/8
                || (b0 == 100 && b1 >= 64 && b1 <= 127)             // 100.64.0.0/10 CGNAT
                || b0 == 127                                        // 127.0.0.0/8 loopback
                || (b0 == 169 && b1 == 254)                         // 169.254.0.0/16 link-local / cloud metadata
                || (b0 == 172 && b1 >= 16 && b1 <= 31)              // 172.16.0.0/12
                || (b0 == 192 && b1 == 0 && bytes[2] == 0)          // 192.0.0.0/24
                || (b0 == 192 && b1 == 168)                         // 192.168.0.0/16
                || (b0 == 198 && (b1 == 18 || b1 == 19))            // benchmark
                || b0 >= 224;                                       // multicast / reserved
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
                return true;
            // fc00::/7 Unique Local Address
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
            // 64:ff9b::/96 NAT64 well-known prefix
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b
                && bytes[4..12].All(b => b == 0))
                return true;
            // 2002::/16 6to4 (can encapsulate private IPv4)
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
                return true;
            // 2001:0000::/32 Teredo
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
                return true;
            // ::/96 deprecated IPv4-compatible IPv6 (includes :: and ::1)
            if (bytes[..12].All(b => b == 0))
                return true;
        }

        return false;
    }

    public async Task<ImportedListingDraft> ImportFromUrlAsync(string rawUrl, CancellationToken ct = default)
    {
        if (!ValidatePublicHttpUri(rawUrl, out var uri, out var error) || uri is null)
        {
            return EmptyFailure(rawUrl ?? "", error ?? "Invalid listing URL.");
        }

        var slugDraft = ExtractFromUrlSlug(uri);

        try
        {
            using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            totalCts.CancelAfter(TotalRequestTimeout);

            var html = await FetchHtmlSafelyAsync(uri, totalCts.Token);
            if (html is null)
            {
                return slugDraft with
                {
                    Succeeded = true,
                    FetchBlocked = true,
                    Notice = $"{uri.Host} blocked automated link preview (or timed out). We pre-filled what we could from your URL — review/complete the remaining fields below and publish for $0."
                };
            }

            var parsed = ParseHtmlMetadata(html, uri);
            return MergeDrafts(parsed, slugDraft);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listing link import fallback for host {Host}", uri.Host);
            return slugDraft with
            {
                Succeeded = true,
                FetchBlocked = true,
                Notice = $"Could not fetch live preview from {uri.Host}. We pre-filled details from your link — review and publish for $0."
            };
        }
    }

    private async Task<string?> FetchHtmlSafelyAsync(Uri initialUri, CancellationToken ct)
    {
        var currentUri = initialUri;
        var startedHttps = currentUri.Scheme == Uri.UriSchemeHttps;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!await AllResolvedIpsArePublicAsync(currentUri.Host, ct))
            {
                _logger.LogWarning("Blocked non-public DNS resolution for {Host}", currentUri.Host);
                return null;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, currentUri);
            req.Headers.TryAddWithoutValidation("User-Agent", "unrealestate.au-link-preview/1.0 (+https://unrealestate.au)");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null)
            {
                if (hop == MaxRedirects) return null;
                var next = resp.Headers.Location.IsAbsoluteUri
                    ? resp.Headers.Location
                    : new Uri(currentUri, resp.Headers.Location);
                // Never allow HTTPS -> HTTP downgrade on redirects
                if (!ValidatePublicHttpUri(next.AbsoluteUri, out var validatedNext, out _, requireHttps: startedHttps)
                    || validatedNext is null)
                {
                    return null;
                }
                currentUri = validatedNext;
                continue;
            }

            if (!resp.IsSuccessStatusCode)
                return null;

            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrEmpty(mediaType)
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                var toCopy = Math.Min(read, MaxResponseBytes - total);
                ms.Write(buffer, 0, toCopy);
                total += toCopy;
                if (total >= MaxResponseBytes) break;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        return null;
    }

    private async Task<bool> AllResolvedIpsArePublicAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip))
            return !IsPrivateOrReservedAddress(ip);

        var addresses = await _dnsResolver(host, ct);
        if (addresses is null || addresses.Length == 0)
            return false;

        return addresses.All(a => !IsPrivateOrReservedAddress(a));
    }

    public static ImportedListingDraft ParseHtmlMetadata(string html, Uri sourceUri)
    {
        string? address = null;
        string? suburb = null;
        string? state = null;
        string? postcode = null;
        string? propertyType = null;
        int? beds = null;
        int? baths = null;
        int? cars = null;
        int? land = null;
        decimal? askingPrice = null;
        string? priceDisplay = null;
        string? headline = null;
        string? description = null;
        string? rawJsonLdImage = null;
        string? agency = null;

        try
        {
            foreach (Match m in JsonLdRegex().Matches(html))
            {
                var json = m.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(json)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    ExtractFromJsonLdElement(
                        doc.RootElement,
                        ref address, ref suburb, ref state, ref postcode,
                        ref propertyType, ref beds, ref baths, ref cars, ref land,
                        ref askingPrice, ref priceDisplay, ref headline, ref description,
                        ref rawJsonLdImage, ref agency);
                }
                catch
                {
                    // Ignore malformed JSON-LD blocks on third-party pages.
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Ignore pathological HTML blocks safely.
        }

        var ogTitle = ExtractMeta(html, "og:title") ?? ExtractTitleTag(html);
        var ogDesc = ExtractMeta(html, "og:description") ?? ExtractMeta(html, "description");
        var ogImage = ExtractMeta(html, "og:image");
        var ogSite = ExtractMeta(html, "og:site_name");

        headline ??= CleanText(ogTitle, 200);
        description ??= CleanText(ogDesc, 1200);
        // Sanitize JSON-LD image BEFORE falling back to og:image so an invalid JSON-LD image never blocks og:image.
        var heroClean = SanitizeImageUrl(rawJsonLdImage, sourceUri) ?? SanitizeImageUrl(ogImage, sourceUri);
        agency ??= CleanText(ogSite, 120);

        var combinedText = $"{headline} {description}";

        if (string.IsNullOrWhiteSpace(suburb) || string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(postcode))
        {
            try
            {
                var addrMatch = AuAddressRegex().Match(combinedText);
                if (addrMatch.Success)
                {
                    address ??= CleanText(addrMatch.Groups["street"].Value, 200);
                    suburb ??= CleanText(addrMatch.Groups["suburb"].Value, 100);
                    state ??= addrMatch.Groups["state"].Value.Trim().ToUpperInvariant();
                    postcode ??= addrMatch.Groups["postcode"].Value.Trim();
                }
            }
            catch (RegexMatchTimeoutException) { }
        }

        beds ??= ExtractIntSpec(BedsRegex(), combinedText, 1, 20);
        baths ??= ExtractIntSpec(BathsRegex(), combinedText, 1, 15);
        cars ??= ExtractIntSpec(CarsRegex(), combinedText, 0, 20);
        land ??= ExtractIntSpec(LandRegex(), combinedText, 30, 500_000);

        if (!askingPrice.HasValue)
        {
            var (parsedPrice, display) = ExtractPrice(combinedText);
            askingPrice = parsedPrice;
            priceDisplay ??= display;
        }
        else if (string.IsNullOrWhiteSpace(priceDisplay))
        {
            priceDisplay = $"${askingPrice.Value:N0}";
        }

        propertyType ??= DetectPropertyType(combinedText);

        return new ImportedListingDraft(
            Succeeded: true,
            FetchBlocked: false,
            SourceUrl: sourceUri.AbsoluteUri,
            SourceHost: sourceUri.Host,
            Address: address,
            Suburb: suburb,
            State: NormalizeState(state),
            Postcode: postcode,
            PropertyType: propertyType ?? "House",
            Bedrooms: beds,
            Bathrooms: baths,
            CarSpaces: cars,
            LandSize: land,
            AskingPrice: askingPrice,
            PriceDisplay: CleanText(priceDisplay, 100),
            Headline: CleanText(headline, 200),
            Description: description,
            HeroImageUrl: heroClean,
            AgencyName: agency,
            Notice: $"Imported listing details from {sourceUri.Host}. Review and confirm before publishing for $0.");
    }

    public static ImportedListingDraft ExtractFromUrlSlug(Uri sourceUri)
    {
        var path = Uri.UnescapeDataString(sourceUri.AbsolutePath);
        var tokens = path.Split(['/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? state = null;
        string? postcode = null;
        string? suburb = null;
        string? address = null;
        string propertyType = DetectPropertyType(path) ?? "House";

        for (var i = 0; i < tokens.Length; i++)
        {
            if (ValidAuStates.Contains(tokens[i]))
            {
                state = tokens[i].ToUpperInvariant();
                if (i + 1 < tokens.Length && PostcodeRegex().IsMatch(tokens[i + 1]))
                {
                    postcode = tokens[i + 1];
                }

                if (i + 1 < tokens.Length && !PostcodeRegex().IsMatch(tokens[i + 1]) && !int.TryParse(tokens[i + 1], out _))
                {
                    var subWords = new List<string>();
                    for (var j = i + 1; j < tokens.Length && subWords.Count < 3; j++)
                    {
                        if (int.TryParse(tokens[j], out _) || PostcodeRegex().IsMatch(tokens[j])) break;
                        subWords.Add(ToTitle(tokens[j]));
                    }
                    if (subWords.Count > 0)
                        suburb = string.Join(" ", subWords);
                }
                else if (i > 0)
                {
                    var streetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "st", "street", "rd", "road", "ave", "avenue", "tce", "terrace",
                        "dr", "drive", "ct", "court", "pl", "place", "ln", "lane", "cres", "crescent", "pde", "parade"
                    };
                    var streetEnd = -1;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        if (streetTypes.Contains(tokens[j]))
                        {
                            streetEnd = j;
                            break;
                        }
                    }

                    if (streetEnd >= 0 && streetEnd < i - 1)
                    {
                        suburb = string.Join(" ", tokens[(streetEnd + 1)..i].Select(ToTitle));
                        var startIdx = Math.Max(0, streetEnd - 3);
                        for (var k = streetEnd - 1; k >= 0; k--)
                        {
                            if (char.IsDigit(tokens[k][0]))
                            {
                                startIdx = k;
                                break;
                            }
                        }
                        address = string.Join(" ", tokens[startIdx..(streetEnd + 1)].Select(ToTitle));
                    }
                    else
                    {
                        suburb = ToTitle(tokens[i - 1]);
                    }
                }
                break;
            }
        }

        return new ImportedListingDraft(
            Succeeded: true,
            FetchBlocked: false,
            SourceUrl: sourceUri.AbsoluteUri,
            SourceHost: sourceUri.Host,
            Address: CleanText(address, 200),
            Suburb: CleanText(suburb, 100),
            State: state,
            Postcode: postcode,
            PropertyType: propertyType,
            Bedrooms: null,
            Bathrooms: null,
            CarSpaces: null,
            LandSize: null,
            AskingPrice: null,
            PriceDisplay: null,
            Headline: CleanText(address is not null && suburb is not null ? $"{address}, {suburb}" : null, 200),
            Description: null,
            HeroImageUrl: null,
            AgencyName: sourceUri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase),
            Notice: null);
    }

    public static string? SanitizeImageUrl(string? raw, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var decoded = WebUtility.HtmlDecode(raw.Trim());
        if (!Uri.TryCreate(baseUri, decoded, out var resolved)) return null;

        // Enforce full public HTTPS host check (blocks localhost, .local, .internal, private IPs, custom ports, credentials).
        if (!ValidatePublicHttpUri(resolved.AbsoluteUri, out var validated, out _, requireHttps: true) || validated is null)
            return null;

        var abs = validated.AbsoluteUri;
        // Reject characters that could break attribute/CSS contexts if ever rendered unsafely.
        if (abs.IndexOfAny(['\'', '"', '(', ')', ';', '<', '>', '\r', '\n']) >= 0)
            return null;

        return abs;
    }

    private static ImportedListingDraft MergeDrafts(ImportedListingDraft primary, ImportedListingDraft fallback) =>
        primary with
        {
            Address = FirstNonEmpty(primary.Address, fallback.Address),
            Suburb = FirstNonEmpty(primary.Suburb, fallback.Suburb),
            State = FirstNonEmpty(primary.State, fallback.State),
            Postcode = FirstNonEmpty(primary.Postcode, fallback.Postcode),
            PropertyType = FirstNonEmpty(primary.PropertyType, fallback.PropertyType) ?? "House",
            Headline = CleanText(FirstNonEmpty(primary.Headline, fallback.Headline), 200),
            AgencyName = FirstNonEmpty(primary.AgencyName, fallback.AgencyName),
        };

    private static void ExtractFromJsonLdElement(
        JsonElement el,
        ref string? address, ref string? suburb, ref string? state, ref string? postcode,
        ref string? propertyType, ref int? beds, ref int? baths, ref int? cars, ref int? land,
        ref decimal? askingPrice, ref string? priceDisplay, ref string? headline, ref string? description,
        ref string? heroImage, ref string? agency)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                ExtractFromJsonLdElement(
                    item, ref address, ref suburb, ref state, ref postcode,
                    ref propertyType, ref beds, ref baths, ref cars, ref land,
                    ref askingPrice, ref priceDisplay, ref headline, ref description,
                    ref heroImage, ref agency);
            }
            return;
        }

        if (el.ValueKind != JsonValueKind.Object) return;

        if (el.TryGetProperty("@graph", out var graph))
        {
            ExtractFromJsonLdElement(
                graph, ref address, ref suburb, ref state, ref postcode,
                ref propertyType, ref beds, ref baths, ref cars, ref land,
                ref askingPrice, ref priceDisplay, ref headline, ref description,
                ref heroImage, ref agency);
        }

        headline ??= CleanText(GetString(el, "name") ?? GetString(el, "headline"), 200);
        description ??= CleanText(GetString(el, "description"), 1200);
        beds ??= GetInt(el, "numberOfBedrooms") ?? GetInt(el, "numberOfRooms");
        baths ??= GetInt(el, "numberOfBathroomsTotal") ?? GetInt(el, "numberOfFullBathrooms");
        cars ??= GetInt(el, "numberOfParkingSpaces");

        if (el.TryGetProperty("@type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            var detected = DetectPropertyType(typeProp.GetString());
            if (detected is not null) propertyType ??= detected;
        }

        if (el.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.Object)
        {
            address ??= CleanText(GetString(addrEl, "streetAddress"), 200);
            suburb ??= CleanText(GetString(addrEl, "addressLocality"), 100);
            state ??= NormalizeState(GetString(addrEl, "addressRegion"));
            postcode ??= CleanText(GetString(addrEl, "postalCode"), 10);
        }

        if (el.TryGetProperty("offers", out var offersEl))
        {
            var offerObj = offersEl.ValueKind == JsonValueKind.Array && offersEl.GetArrayLength() > 0
                ? offersEl[0]
                : offersEl;
            if (offerObj.ValueKind == JsonValueKind.Object)
            {
                var p = GetDecimal(offerObj, "price") ?? GetDecimal(offerObj, "lowPrice");
                if (p.HasValue && p.Value >= 50_000m)
                {
                    askingPrice ??= p.Value;
                    priceDisplay ??= $"${p.Value:N0}";
                }
                if (offerObj.TryGetProperty("seller", out var seller) && seller.ValueKind == JsonValueKind.Object)
                    agency ??= CleanText(GetString(seller, "name"), 120);
            }
        }

        if (el.TryGetProperty("floorSize", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Object)
            land ??= GetInt(sizeEl, "value");

        if (heroImage is null && el.TryGetProperty("image", out var imgEl))
        {
            if (imgEl.ValueKind == JsonValueKind.String)
                heroImage = imgEl.GetString();
            else if (imgEl.ValueKind == JsonValueKind.Array && imgEl.GetArrayLength() > 0 && imgEl[0].ValueKind == JsonValueKind.String)
                heroImage = imgEl[0].GetString();
            else if (imgEl.ValueKind == JsonValueKind.Object)
                heroImage = GetString(imgEl, "url");
        }
    }

    private static string? ExtractMeta(string html, string key)
    {
        try
        {
            foreach (Match m in MetaTagRegex().Matches(html))
            {
                var tag = m.Value;
                if (!tag.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase)
                    && !tag.Contains($"'{key}'", StringComparison.OrdinalIgnoreCase))
                    continue;

                var contentMatch = ContentAttrRegex().Match(tag);
                if (contentMatch.Success)
                    return WebUtility.HtmlDecode(contentMatch.Groups[2].Value);
            }
        }
        catch (RegexMatchTimeoutException) { }
        return null;
    }

    private static string? ExtractTitleTag(string html)
    {
        try
        {
            var m = TitleTagRegex().Match(html);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static (decimal? Price, string? Display) ExtractPrice(string text)
    {
        try
        {
            var mMill = PriceMillionsRegex().Match(text);
            if (mMill.Success && decimal.TryParse(mMill.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var millions))
            {
                var val = Math.Round(millions * 1_000_000m, 0);
                if (val >= 100_000m && val <= 100_000_000m)
                    return (val, mMill.Value.Trim());
            }

            var mFull = PriceDollarsRegex().Match(text);
            if (mFull.Success)
            {
                var digits = mFull.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)
                    && dollars >= 100_000m && dollars <= 100_000_000m)
                {
                    return (dollars, $"${dollars:N0}");
                }
            }
        }
        catch (RegexMatchTimeoutException) { }

        return (null, null);
    }

    private static int? ExtractIntSpec(Regex rx, string text, int min, int max)
    {
        try
        {
            var m = rx.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                if (v >= min && v <= max) return v;
            }
        }
        catch (RegexMatchTimeoutException) { }
        return null;
    }

    private static string? DetectPropertyType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("townhouse", StringComparison.OrdinalIgnoreCase)) return "Townhouse";
        if (text.Contains("apartment", StringComparison.OrdinalIgnoreCase)) return "Apartment";
        if (text.Contains("unit", StringComparison.OrdinalIgnoreCase)) return "Unit";
        if (text.Contains("villa", StringComparison.OrdinalIgnoreCase)) return "Villa";
        if (text.Contains("land", StringComparison.OrdinalIgnoreCase) && !text.Contains("queensland", StringComparison.OrdinalIgnoreCase)) return "Land";
        if (text.Contains("house", StringComparison.OrdinalIgnoreCase) || text.Contains("residence", StringComparison.OrdinalIgnoreCase)) return "House";
        return null;
    }

    private static string? NormalizeState(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var up = s.Trim().ToUpperInvariant();
        return ValidAuStates.Contains(up) ? up : null;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static decimal? GetDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString()?.Replace("$", "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var s))
            return s;
        return null;
    }

    private static string? CleanText(string? s, int maxLen = 1200)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var collapsed = WhitespaceRegex().Replace(WebUtility.HtmlDecode(s), " ").Trim();
        return collapsed.Length > maxLen ? collapsed[..maxLen] : collapsed;
    }

    private static string? FirstNonEmpty(params string?[] vals) =>
        vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string ToTitle(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private static ImportedListingDraft EmptyFailure(string rawUrl, string message) => new(
        Succeeded: false,
        FetchBlocked: false,
        SourceUrl: rawUrl,
        SourceHost: "",
        Address: null,
        Suburb: null,
        State: null,
        Postcode: null,
        PropertyType: "House",
        Bedrooms: null,
        Bathrooms: null,
        CarSpaces: null,
        LandSize: null,
        AskingPrice: null,
        PriceDisplay: null,
        Headline: null,
        Description: null,
        HeroImageUrl: null,
        AgencyName: null,
        Notice: message);

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 500)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex(@"<meta\b[^>]{1,1000}>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(@"content\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 500)]
    private static partial Regex ContentAttrRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 500)]
    private static partial Regex TitleTagRegex();

    [GeneratedRegex(@"(?<street>\d+[A-Za-z]?(?:/\d+[A-Za-z]?)?\s+[A-Za-z0-9\s\-']{2,60}?(?:St|Street|Rd|Road|Ave|Avenue|Tce|Terrace|Dr|Drive|Ct|Court|Pl|Place|Ln|Lane|Cres|Crescent|Pde|Parade))\s*,?\s+(?<suburb>[A-Za-z\s\-']{2,35}?)\s*,?\s+(?<state>QLD|NSW|VIC|WA|SA|TAS|ACT|NT)\s+(?<postcode>\d{4})", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex AuAddressRegex();

    [GeneratedRegex(@"^\d{4}$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex PostcodeRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:bed(?:room)?s?|bd)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 300)]
    private static partial Regex BedsRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:bath(?:room)?s?|ba)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 300)]
    private static partial Regex BathsRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:car(?:\s*space)?s?|garage|parking)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 300)]
    private static partial Regex CarsRegex();

    [GeneratedRegex(@"\b(\d{2,6})\s*(?:m2|m²|sqm)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 300)]
    private static partial Regex LandRegex();

    [GeneratedRegex(@"\$(\d+(?:\.\d{1,2})?)\s*[mM]\b", RegexOptions.None, matchTimeoutMilliseconds: 300)]
    private static partial Regex PriceMillionsRegex();

    [GeneratedRegex(@"\$([1-9]\d{2}(?:,\d{3})+|\d{6,8})\b", RegexOptions.None, matchTimeoutMilliseconds: 300)]
    private static partial Regex PriceDollarsRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 300)]
    private static partial Regex WhitespaceRegex();
}
