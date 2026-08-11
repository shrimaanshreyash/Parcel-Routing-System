using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ParcelRoutingSystem.Api.Configuration;

namespace ParcelRoutingSystem.Api.IntegrationTests;

/// <summary>
/// Exercises the final HTTP security contract through the composed API rather
/// than treating configuration or compilation as runtime evidence.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public sealed class FinalSecurityValidationTests
{
    private readonly ApiIntegrationFixture _fixture;

    /// <summary>
    /// Creates the security validation suite around the shared disposable API
    /// and PostgreSQL fixture.
    /// </summary>
    /// <param name="fixture">The real migrated test host boundary.</param>
    public FinalSecurityValidationTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Proves all reviewed API hardening headers are present on a real response.
    /// </summary>
    [Fact]
    public async Task Response_WhenApiIsCalled_IncludesSecurityHeaders()
    {
        using HttpClient client = CreateClient(_fixture.Factory);

        using HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal(
            "camera=(), microphone=(), geolocation=()",
            Header(response, "Permissions-Policy"));
        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'",
            Header(response, "Content-Security-Policy"));
    }

    /// <summary>
    /// Proves domain rejection uses bounded Problem Details without stack,
    /// exception, or request-body disclosure.
    /// </summary>
    [Fact]
    public async Task Error_WhenParcelIsInvalid_ReturnsSafeProblemDetails()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/parcels/route")
        {
            Content = JsonContent.Create(
                new
                {
                    weightKilograms = 1m,
                    declaredValueEuros = 10m,
                    destinationCountry = "ZZ",
                    operatorReference = "SHOULD-NOT-BE-ECHOED",
                }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "parcel.country.invalid",
            problem.GetProperty("code").GetString());
        string serialized = problem.GetRawText();
        Assert.DoesNotContain("SHOULD-NOT-BE-ECHOED", serialized);
        Assert.DoesNotContain("stack", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", serialized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Proves the HTTP byte ceiling rejects an oversized XML body before parsing
    /// or durable batch creation.
    /// </summary>
    [Fact]
    public async Task Import_WhenBodyExceedsTwoMebibytes_ReturnsPayloadTooLarge()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        string xml = "<Container><parcels><Parcel><Weight>1</Weight><Value>10</Value>"
            + "<Country>GB</Country><Receipient><Note>"
            + new string('x', 2_100_000)
            + "</Note></Receipient></Parcel></parcels></Container>";
        using var request = XmlRequest(xml, "over-two-mebibytes.xml");

        using HttpResponseMessage response = await client.SendAsync(request);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(
            "routing.manifest.limit_exceeded",
            problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Proves the parser's independent two-million-character ceiling rejects a
    /// document that is still below the HTTP two-mebibyte byte ceiling.
    /// </summary>
    [Fact]
    public async Task Import_WhenBodyExceedsTwoMillionCharacters_ReturnsPayloadTooLarge()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        string xml = "<Container><parcels><Parcel><Weight>1</Weight><Value>10</Value>"
            + "<Country>GB</Country><Receipient><Note>"
            + new string('x', 2_000_001)
            + "</Note></Receipient></Parcel></parcels></Container>";
        Assert.True(Encoding.UTF8.GetByteCount(xml) < 2_097_152);
        using var request = XmlRequest(xml, "over-two-million-characters.xml");

        using HttpResponseMessage response = await client.SendAsync(request);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(
            "routing.manifest.limit_exceeded",
            problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Proves the parser's independent ten-thousand-row ceiling rejects the
    /// entire document even when its byte and character sizes remain permitted.
    /// </summary>
    [Fact]
    public async Task Import_WhenBodyExceedsTenThousandRows_ReturnsPayloadTooLarge()
    {
        using HttpClient client = CreateClient(_fixture.Factory);
        const string row =
            "<Parcel><Weight>1</Weight><Value>10</Value><Country>GB</Country></Parcel>";
        string xml = "<Container><parcels>"
            + string.Concat(Enumerable.Repeat(row, 10_001))
            + "</parcels></Container>";
        using var request = XmlRequest(xml, "over-ten-thousand-rows.xml");

        using HttpResponseMessage response = await client.SendAsync(request);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(
            "routing.manifest.limit_exceeded",
            problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Proves the authenticated query policy returns a stable 429 response and
    /// Retry-After evidence after its bounded fixed window is exhausted.
    /// </summary>
    [Fact]
    public async Task Query_WhenRateWindowIsExhausted_ReturnsTooManyRequests()
    {
        using WebApplicationFactory<Program> rateFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder => builder.UseSetting(
                    "RateLimits:QueryPermitLimit",
                    "3"));
        using HttpClient client = CreateClient(rateFactory);
        HttpResponseMessage? rejected = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            HttpResponseMessage response = await client.GetAsync("/api/rules/active");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }

            response.Dispose();
        }

        Assert.NotNull(rejected);
        using (rejected)
        {
            JsonElement problem =
                await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("60", Header(rejected, "Retry-After"));
            Assert.Equal(
                "http.rate_limit.exceeded",
                problem.GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// Proves the InsuranceApprover capability alone cannot perform the separate
    /// Operator parcel-routing capability.
    /// </summary>
    [Fact]
    public async Task Route_WhenIdentityHasOnlyApproverRole_ReturnsForbidden()
    {
        using WebApplicationFactory<Program> approverFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder =>
                {
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:0",
                        "InsuranceApprover");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:1",
                        "InsuranceApprover");
                    builder.UseSetting(
                        "ParcelAuthentication:DevelopmentRoles:2",
                        "InsuranceApprover");
                });
        using HttpClient client = CreateClient(approverFactory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/parcels/route")
        {
            Content = JsonContent.Create(
                new
                {
                    weightKilograms = 1m,
                    declaredValueEuros = 10m,
                    destinationCountry = "GB",
                }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Proves a malformed bearer token receives 401 from the real production
    /// JWT handler when provider metadata is supplied in-process for the test.
    /// </summary>
    [Fact]
    public async Task Production_WhenBearerTokenIsMalformed_ReturnsUnauthorized()
    {
        using WebApplicationFactory<Program> productionFactory =
            CreateProductionJwtFactory();
        using HttpClient client = CreateClient(
            productionFactory,
            new Uri("https://localhost"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        using HttpResponseMessage response = await client.GetAsync(
            "/api/rules/active");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Proves the host refuses Development authentication before accepting
    /// traffic in a Production environment.
    /// </summary>
    [Fact]
    public void Production_WhenDevelopmentAuthenticationIsConfigured_RefusesStartup()
    {
        using WebApplicationFactory<Program> unsafeFactory =
            _fixture.Factory.WithWebHostBuilder(
                builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(
                        "Database:ApplyMigrationsOnStartup",
                        "false");
                    builder.UseSetting(
                        "ParcelAuthentication:Mode",
                        "Development");
                });

        Exception exception = Assert.ThrowsAny<Exception>(
            () => unsafeFactory.CreateClient());

        Assert.Contains(
            "Development authentication is prohibited outside Development",
            Flatten(exception),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves forwarding is one-hop and opt-in to an explicit CIDR rather than
    /// trusting client-supplied forwarding headers from arbitrary addresses.
    /// </summary>
    [Fact]
    public void ReverseProxy_WhenTrustedNetworkIsConfigured_UsesBoundedOptions()
    {
        var values = new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownNetworks:0"] = "10.20.0.0/16",
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddParcelRoutingForwardedHeaders(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        ForwardedHeadersOptions options = provider
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Equal(
            System.Net.IPNetwork.Parse("10.20.0.0/16"),
            Assert.Single(options.KnownIPNetworks));
        Assert.Empty(options.KnownProxies);
    }

    /// <summary>
    /// Creates a Production host that preserves the real JWT bearer handler but
    /// supplies static issuer metadata so invalid-token behavior is deterministic
    /// and never depends on a network identity provider.
    /// </summary>
    private WebApplicationFactory<Program> CreateProductionJwtFactory()
    {
        return _fixture.Factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
                builder.UseSetting("ParcelAuthentication:Mode", "OidcJwt");
                builder.UseSetting(
                    "ParcelAuthentication:Authority",
                    "https://issuer.test");
                builder.UseSetting(
                    "ParcelAuthentication:Audience",
                    "parcel-routing-api");
                builder.ConfigureServices(
                    services => services.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options =>
                        {
                            options.Configuration = new OpenIdConnectConfiguration
                            {
                                Issuer = "https://issuer.test",
                            };
                        }));
            });
    }

    /// <summary>
    /// Creates a bounded XML request with the required safe transport metadata.
    /// </summary>
    /// <param name="xml">The controlled privacy-safe XML text.</param>
    /// <param name="fileName">The safe manifest filename.</param>
    /// <returns>The disposable import request.</returns>
    private static HttpRequestMessage XmlRequest(string xml, string fileName)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/batches/import-xml")
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("X-Manifest-Name", fileName);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    /// <summary>
    /// Creates a non-redirecting test client so transport and authorization
    /// status codes remain directly observable.
    /// </summary>
    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        Uri? baseAddress = null)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = baseAddress ?? new Uri("http://localhost"),
            });
    }

    /// <summary>
    /// Reads exactly one expected response header value.
    /// </summary>
    private static string Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? Assert.Single(values)
            : throw new Xunit.Sdk.XunitException(
                $"Expected response header '{name}' was absent.");
    }

    /// <summary>
    /// Flattens nested startup exceptions into one diagnostic string without
    /// relying on a particular host wrapper type.
    /// </summary>
    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
