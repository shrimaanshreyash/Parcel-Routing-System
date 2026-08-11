using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ParcelRoutingSystem.Api.Configuration;

namespace ParcelRoutingSystem.Api.Security;

/// <summary>
/// Provides an explicit Development-only reviewer identity so local evaluation
/// remains usable without pretending a production identity provider exists.
/// </summary>
public sealed class DevelopmentAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Gets the scheme name used only by Development hosts.</summary>
    public const string SchemeName = "ParcelRoutingDevelopment";

    private readonly ParcelAuthenticationOptions _parcelOptions;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Creates the handler with framework scheme services and validated
    /// application authentication configuration.
    /// </summary>
    /// <param name="options">Framework scheme options.</param>
    /// <param name="logger">Framework logger factory.</param>
    /// <param name="encoder">URL encoder required by the authentication base.</param>
    /// <param name="parcelOptions">The configured reviewer identity and roles.</param>
    /// <param name="environment">The current host environment.</param>
    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ParcelAuthenticationOptions> parcelOptions,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        _parcelOptions = parcelOptions.Value;
        _environment = environment;
    }

    /// <summary>
    /// Authenticates the configured reviewer only in Development; when disabled
    /// it returns no identity so authorization correctly produces HTTP 401.
    /// </summary>
    /// <returns>The local reviewer ticket or no-result.</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_environment.IsDevelopment())
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Development authentication cannot run outside Development."));
        }

        if (!_parcelOptions.DevelopmentAutoAuthenticate)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _parcelOptions.DevelopmentActor),
            new(ClaimTypes.Name, "Local reviewer"),
        };
        claims.AddRange(
            _parcelOptions.DevelopmentRoles.Select(
                role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
