using System.ComponentModel.DataAnnotations;

namespace ParcelRoutingSystem.Api.Configuration;

/// <summary>
/// Binds the required PostgreSQL runtime configuration while keeping connection
/// secrets outside committed settings files.
/// </summary>
public sealed class ParcelRoutingDatabaseOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Gets or sets the externally supplied PostgreSQL connection string.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether a Development host applies reviewed migrations at
    /// startup for local reviewer convenience.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

/// <summary>
/// Selects production OIDC access-token validation or the explicit
/// Development-only reviewer identity.
/// </summary>
public sealed class ParcelAuthenticationOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ParcelAuthentication";

    /// <summary>Gets or sets `OidcJwt` or `Development`.</summary>
    [Required]
    public string Mode { get; set; } = "OidcJwt";

    /// <summary>Gets or sets the trusted OIDC authority for production tokens.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Gets or sets the required API audience for production tokens.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the local Development handler automatically creates
    /// the configured reviewer identity.
    /// </summary>
    public bool DevelopmentAutoAuthenticate { get; set; } = true;

    /// <summary>Gets or sets the non-personal local reviewer subject.</summary>
    public string DevelopmentActor { get; set; } = "local-reviewer";

    /// <summary>Gets or sets allow-listed roles granted only in Development.</summary>
    public string[] DevelopmentRoles { get; set; } = [];
}

/// <summary>
/// Defines the explicit proxy networks whose forwarding headers the API may
/// trust when a production TLS terminator sits in front of the private API.
/// </summary>
public sealed class ParcelReverseProxyOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Gets or sets trusted IPv4 or IPv6 networks in CIDR notation. An empty
    /// collection retains the framework's loopback-only defaults.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];
}

/// <summary>
/// Defines cost-specific fixed-window request ceilings with conservative
/// production defaults and validated deployment overrides.
/// </summary>
public sealed class ParcelRateLimitOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "RateLimits";

    /// <summary>Gets or sets parcel-routing requests permitted per window.</summary>
    [Range(1, 10_000)]
    public int RoutingPermitLimit { get; set; } = 60;

    /// <summary>Gets or sets XML uploads permitted per window.</summary>
    [Range(1, 10_000)]
    public int UploadPermitLimit { get; set; } = 10;

    /// <summary>Gets or sets approval or rule-write requests per window.</summary>
    [Range(1, 10_000)]
    public int ApprovalPermitLimit { get; set; } = 30;

    /// <summary>Gets or sets bounded read requests permitted per window.</summary>
    [Range(1, 10_000)]
    public int QueryPermitLimit { get; set; } = 120;

    /// <summary>Gets or sets the fixed-window duration in minutes.</summary>
    [Range(1, 60)]
    public int WindowMinutes { get; set; } = 1;
}

/// <summary>
/// Binds bounded XML parser and HTTP upload limits that are validated at host
/// startup.
/// </summary>
public sealed class ParcelManifestOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Manifest";

    /// <summary>Gets or sets the request body byte limit.</summary>
    [Range(1, 2_097_152)]
    public long MaximumBytes { get; set; } = 2_097_152;

    /// <summary>Gets or sets the supported parcel-row limit.</summary>
    [Range(1, 10_000)]
    public int MaximumRows { get; set; } = 10_000;

    /// <summary>Gets or sets the XML character limit.</summary>
    [Range(1, 2_000_000)]
    public long MaximumCharacters { get; set; } = 2_000_000;

    /// <summary>Gets or sets the parser timeout in seconds.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Binds the in-process durable worker cadence without changing lease or
/// idempotency semantics owned by the application layer.
/// </summary>
public sealed class BatchProcessorOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "BatchProcessor";

    /// <summary>Gets or sets whether the hosted processor claims durable rows.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the empty-queue polling delay in milliseconds.</summary>
    [Range(100, 10_000)]
    public int IdleDelayMilliseconds { get; set; } = 500;

    /// <summary>Gets or sets the unexpected-failure retry delay in milliseconds.</summary>
    [Range(500, 60_000)]
    public int FailureDelayMilliseconds { get; set; } = 2_000;
}
