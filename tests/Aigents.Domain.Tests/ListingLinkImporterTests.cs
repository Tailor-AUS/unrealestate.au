using System.Net;
using System.Text;
using Aigents.Infrastructure.Services.Import;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aigents.Domain.Tests;

public class ListingLinkImporterTests
{
    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null) RequestedUris.Add(request.RequestUri);
            return handler(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Theory]
    [InlineData("http://localhost/listing")]
    [InlineData("https://127.0.0.1/listing")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("https://192.168.1.10/admin")]
    [InlineData("https://172.16.0.9/secret")]
    [InlineData("https://[::1]/listing")]
    [InlineData("https://[fc00::1]/listing")]
    [InlineData("https://[fe80::1]/listing")]
    [InlineData("https://service.internal/listing")]
    [InlineData("https://nas.local/listing")]
    [InlineData("https://user:pass@agency.com.au/listing")]
    [InlineData("https://agency.com.au:8443/listing")]
    [InlineData("ftp://agency.com.au/listing")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void ValidatePublicHttpUri_RejectsPrivateCustomPortAndUnsafeUrls(string raw)
    {
        var ok = ListingLinkImporter.ValidatePublicHttpUri(raw, out var uri, out var error);
        Assert.False(ok);
        Assert.Null(uri);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("64:ff9b::169.254.169.254")]
    [InlineData("2002:7f00:0001::1")]
    [InlineData("2001:0000:4136:e378::1")]
    [InlineData("::127.0.0.1")]
    public void IsPrivateOrReservedAddress_BlocksSpecialIpv6AndCgnatRanges(string ipText)
    {
        Assert.True(ListingLinkImporter.IsPrivateOrReservedAddress(IPAddress.Parse(ipText)));
        Assert.False(ListingLinkImporter.IsPrivateOrReservedAddress(IPAddress.Parse("1.1.1.1")));
    }

    [Theory]
    [InlineData("https://localhost/hero.jpg")]
    [InlineData("https://intranet.local/hero.jpg")]
    [InlineData("http://cdn.agency.com.au/hero.jpg")]
    [InlineData("https://cdn.agency.com.au/hero.jpg');background:red")]
    [InlineData("https://cdn.agency.com.au:8443/hero.jpg")]
    public void SanitizeImageUrl_RejectsInternalNonHttpsAndCssInjectionUrls(string raw)
    {
        Assert.Null(ListingLinkImporter.SanitizeImageUrl(raw, new Uri("https://agency.example.com.au/")));
    }

    [Fact]
    public async Task ImportFromUrlAsync_BlocksDnsResolvingToPrivateIp_WithoutMakingHttpCall()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Must never issue HTTP request when DNS resolves to private IP"));

        var importer = new ListingLinkImporter(
            new StubHttpClientFactory(handler),
            NullLogger<ListingLinkImporter>.Instance,
            dnsResolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse("169.254.169.254") }));

        var res = await importer.ImportFromUrlAsync("https://rebind.example.com/42-latrobe-terrace-paddington-qld-4064");
        Assert.True(res.FetchBlocked);
        Assert.Empty(handler.RequestedUris);
        Assert.Equal("QLD", res.State);
        Assert.Equal("4064", res.Postcode);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://agency.example.com.au/downgraded")]
    public async Task ImportFromUrlAsync_BlocksMetadataAndHttpsDowngradeRedirects(string redirectLocation)
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.Scheme == Uri.UriSchemeHttps && req.RequestUri.Host == "agency.example.com.au")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri(redirectLocation);
                return Task.FromResult(redirect);
            }
            throw new InvalidOperationException($"Must not follow unsafe redirect to {redirectLocation}");
        });

        var importer = new ListingLinkImporter(
            new StubHttpClientFactory(handler),
            NullLogger<ListingLinkImporter>.Instance,
            dnsResolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var res = await importer.ImportFromUrlAsync("https://agency.example.com.au/42-latrobe-terrace-paddington-qld-4064");
        Assert.True(res.FetchBlocked);
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task ImportFromUrlAsync_ExtractsJsonLdAndFallsBackFromInvalidJsonLdImageToOgImage()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <meta property="og:title" content="42 Latrobe Terrace, Paddington QLD 4064" />
                <meta property="og:description" content="Renovated Queenslander with 4 bedrooms, 3 bathrooms, 2 car spaces on 486m2." />
                <meta property="og:image" content="https://cdn.agency.example.com.au/hero.jpg" />
                <meta property="og:site_name" content="Harbourline Realty" />
                <script type="application/ld+json">
                {
                  "@context": "https://schema.org",
                  "@type": "SingleFamilyResidence",
                  "name": "42 Latrobe Terrace, Paddington QLD 4064",
                  "image": "http://insecure.invalid/bad.jpg",
                  "numberOfBedrooms": 4,
                  "numberOfBathroomsTotal": 3,
                  "numberOfParkingSpaces": 2,
                  "address": {
                    "@type": "PostalAddress",
                    "streetAddress": "42 Latrobe Terrace",
                    "addressLocality": "Paddington",
                    "addressRegion": "QLD",
                    "postalCode": "4064"
                  },
                  "offers": {
                    "@type": "Offer",
                    "price": 1650000
                  }
                }
                </script>
              </head>
              <body></body>
            </html>
            """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            }));

        var importer = new ListingLinkImporter(
            new StubHttpClientFactory(handler),
            NullLogger<ListingLinkImporter>.Instance,
            dnsResolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var draft = await importer.ImportFromUrlAsync("https://agency.example.com.au/42-latrobe-terrace-paddington-qld-4064");

        Assert.True(draft.Succeeded);
        Assert.False(draft.FetchBlocked);
        Assert.Equal("42 Latrobe Terrace", draft.Address);
        Assert.Equal("Paddington", draft.Suburb);
        Assert.Equal("QLD", draft.State);
        Assert.Equal("4064", draft.Postcode);
        Assert.Equal(4, draft.Bedrooms);
        Assert.Equal(3, draft.Bathrooms);
        Assert.Equal(2, draft.CarSpaces);
        Assert.Equal(486, draft.LandSize);
        Assert.Equal(1_650_000m, draft.AskingPrice);
        Assert.Equal("https://cdn.agency.example.com.au/hero.jpg", draft.HeroImageUrl);
        Assert.Equal("Harbourline Realty", draft.AgencyName);
    }

    [Fact]
    public async Task ImportFromUrlAsync_WhenRemoteSiteBlocksFetch_FallsBackHonestlyToUrlSlug()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var importer = new ListingLinkImporter(
            new StubHttpClientFactory(handler),
            NullLogger<ListingLinkImporter>.Instance,
            dnsResolver: (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var draft = await importer.ImportFromUrlAsync("https://portal.example.com.au/88-oxlade-drive-new-farm-qld-4005");

        Assert.True(draft.Succeeded);
        Assert.True(draft.FetchBlocked);
        Assert.Equal("88 Oxlade Drive", draft.Address);
        Assert.Equal("New Farm", draft.Suburb);
        Assert.Equal("QLD", draft.State);
        Assert.Equal("4005", draft.Postcode);
        Assert.Contains("blocked automated link preview", draft.Notice, StringComparison.OrdinalIgnoreCase);
    }
}
