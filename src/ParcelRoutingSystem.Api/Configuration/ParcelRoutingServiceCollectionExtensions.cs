using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Api.Workers;
using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Imports;
using ParcelRoutingSystem.Application.Operations;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Infrastructure;
using ParcelRoutingSystem.Infrastructure.Persistence;
using ParcelRoutingSystem.Infrastructure.Xml;

namespace ParcelRoutingSystem.Api.Configuration;

/// <summary>
/// Composes framework, application, and infrastructure services while keeping
/// Program readable and all environment decisions explicit.
/// </summary>
public static class ParcelRoutingServiceCollectionExtensions
{
    /// <summary>
    /// Registers validated options, PostgreSQL adapters, application use cases,
    /// secure XML parsing, readiness checks, and the durable hosted processor.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The layered external configuration.</param>
    /// <returns>The same collection for fluent host composition.</returns>
    public static IServiceCollection AddParcelRoutingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ParcelRoutingDatabaseOptions>()
            .Bind(configuration.GetSection(ParcelRoutingDatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ParcelManifestOptions>()
            .Bind(configuration.GetSection(ParcelManifestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<BatchProcessorOptions>()
            .Bind(configuration.GetSection(BatchProcessorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<ParcelRoutingDbContext>(
            (provider, options) =>
            {
                ParcelRoutingDatabaseOptions database = provider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<
                            ParcelRoutingDatabaseOptions>>()
                    .Value;
                options.UseNpgsql(database.ConnectionString);
            });
        services.AddScoped<IRoutingDecisionRepository, PostgresRoutingDecisionRepository>();
        services.AddScoped<IInsuranceApprovalRepository, PostgresInsuranceApprovalRepository>();
        services.AddScoped<IBatchRepository, PostgresBatchRepository>();
        services.AddScoped<IRuleSetRepository, PostgresRuleSetRepository>();
        services.AddScoped<IOperationsReadRepository, PostgresOperationsReadRepository>();
        services.AddSingleton<IApplicationClock, SystemApplicationClock>();
        services.AddSingleton<IIdentifierGenerator, GuidIdentifierGenerator>();
        services.AddScoped<RouteParcelUseCase>();
        services.AddScoped<ApproveInsuranceUseCase>();
        services.AddScoped<CreateBatchUseCase>();
        services.AddScoped<ProcessNextBatchRowUseCase>();
        services.AddScoped<RuleSetLifecycleUseCase>();
        services.AddScoped<OperationsQueryUseCase>();
        services.AddSingleton<IParcelManifestParser>(
            provider =>
            {
                ParcelManifestOptions manifest = provider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<
                            ParcelManifestOptions>>()
                    .Value;
                var limits = new LegacyXmlManifestLimits(
                    manifest.MaximumRows,
                    manifest.MaximumCharacters,
                    TimeSpan.FromSeconds(manifest.TimeoutSeconds));
                return new LegacyXmlParcelManifestParser(limits);
            });
        services.AddHostedService<DurableBatchProcessor>();
        services.AddHealthChecks()
            .AddDbContextCheck<ParcelRoutingDbContext>(
                name: "postgresql",
                tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Registers either the Development-only reviewer scheme or production
    /// OIDC/JWT validation, then applies least-privilege server policies.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">Authentication configuration.</param>
    /// <param name="environment">The current host environment.</param>
    /// <returns>The same collection for fluent host composition.</returns>
    public static IServiceCollection AddParcelRoutingSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        IConfigurationSection section = configuration.GetSection(
            ParcelAuthenticationOptions.SectionName);
        ParcelAuthenticationOptions configured =
            section.Get<ParcelAuthenticationOptions>()
            ?? new ParcelAuthenticationOptions();
        services.AddOptions<ParcelAuthenticationOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(
                options => ValidateAuthenticationOptions(options, environment),
                "Authentication mode and provider values are invalid.")
            .ValidateOnStart();

        if (string.Equals(
                configured.Mode,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Development authentication is prohibited outside Development.");
            }

            services.AddAuthentication(
                    DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<
                    Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                    DevelopmentAuthenticationHandler>(
                    DevelopmentAuthenticationHandler.SchemeName,
                    _ => { });
        }
        else if (string.Equals(
                     configured.Mode,
                     "OidcJwt",
                     StringComparison.OrdinalIgnoreCase))
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(
                    options =>
                    {
                        options.Authority = configured.Authority;
                        options.Audience = configured.Audience;
                        options.RequireHttpsMetadata = true;
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateIssuerSigningKey = true,
                            ValidateLifetime = true,
                            NameClaimType = "name",
                            RoleClaimType = "roles",
                            ClockSkew = TimeSpan.FromMinutes(1),
                        };
                    });
        }
        else
        {
            throw new InvalidOperationException(
                "ParcelAuthentication:Mode must be Development or OidcJwt.");
        }

        services.AddAuthorization(
            options =>
            {
                options.AddPolicy(
                    AuthorizationPolicies.Authenticated,
                    policy => policy.RequireAuthenticatedUser());
                options.AddPolicy(
                    AuthorizationPolicies.Operator,
                    policy => policy.RequireRole(AuthorizationPolicies.OperatorRole));
                options.AddPolicy(
                    AuthorizationPolicies.InsuranceApprover,
                    policy => policy.RequireRole(
                        AuthorizationPolicies.InsuranceApproverRole));
                options.AddPolicy(
                    AuthorizationPolicies.RuleAdministrator,
                    policy => policy.RequireRole(
                        AuthorizationPolicies.RuleAdministratorRole));
            });

        return services;
    }

    /// <summary>
    /// Registers cost-specific fixed-window policies partitioned by authenticated
    /// subject, falling back to remote address before authentication succeeds.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The layered external configuration.</param>
    /// <returns>The same collection for fluent host composition.</returns>
    public static IServiceCollection AddParcelRoutingRateLimits(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ParcelRateLimitOptions>()
            .Bind(configuration.GetSection(ParcelRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddRateLimiter(
            options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = WriteRateLimitProblemAsync;
                options.AddPolicy(
                    ApiRateLimitPolicies.Routing,
                    context => CreateConfiguredPartition(
                        context,
                        limits => limits.RoutingPermitLimit));
                options.AddPolicy(
                    ApiRateLimitPolicies.Upload,
                    context => CreateConfiguredPartition(
                        context,
                        limits => limits.UploadPermitLimit));
                options.AddPolicy(
                    ApiRateLimitPolicies.Approval,
                    context => CreateConfiguredPartition(
                        context,
                        limits => limits.ApprovalPermitLimit));
                options.AddPolicy(
                    ApiRateLimitPolicies.Query,
                    context => CreateConfiguredPartition(
                        context,
                        limits => limits.QueryPermitLimit));
            });

        return services;
    }

    /// <summary>
    /// Resolves validated deployment limits for the current request before
    /// creating its authenticated fixed-window partition.
    /// </summary>
    /// <param name="context">The current request and scoped service provider.</param>
    /// <param name="selectPermitLimit">Selects the cost-specific request ceiling.</param>
    /// <returns>The configured fixed-window partition.</returns>
    private static RateLimitPartition<string> CreateConfiguredPartition(
        HttpContext context,
        Func<ParcelRateLimitOptions, int> selectPermitLimit)
    {
        ParcelRateLimitOptions limits = context.RequestServices
            .GetRequiredService<IOptions<ParcelRateLimitOptions>>()
            .Value;
        return CreateFixedWindowPartition(
            context,
            selectPermitLimit(limits),
            limits.WindowMinutes);
    }

    /// <summary>
    /// Registers one-hop forwarded-protocol and client-address processing while
    /// trusting only explicitly configured proxy networks.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The layered external configuration.</param>
    /// <returns>The same collection for fluent host composition.</returns>
    public static IServiceCollection AddParcelRoutingForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ParcelReverseProxyOptions configured = configuration
            .GetSection(ParcelReverseProxyOptions.SectionName)
            .Get<ParcelReverseProxyOptions>()
            ?? new ParcelReverseProxyOptions();
        System.Net.IPNetwork[] networks = configured.KnownNetworks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ParseKnownNetwork)
            .ToArray();

        services.Configure<ForwardedHeadersOptions>(
            options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor
                    | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                if (networks.Length > 0)
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                    foreach (System.Net.IPNetwork network in networks)
                    {
                        options.KnownIPNetworks.Add(network);
                    }
                }
            });

        return services;
    }

    /// <summary>
    /// Validates environment-sensitive authentication configuration before the
    /// host accepts traffic.
    /// </summary>
    /// <param name="options">The bound authentication settings.</param>
    /// <param name="environment">The current host environment.</param>
    /// <returns>True only for a safe complete mode.</returns>
    private static bool ValidateAuthenticationOptions(
        ParcelAuthenticationOptions options,
        IHostEnvironment environment)
    {
        if (string.Equals(
                options.Mode,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            return environment.IsDevelopment()
                && !string.IsNullOrWhiteSpace(options.DevelopmentActor)
                && options.DevelopmentRoles.All(IsAllowedRole);
        }

        return string.Equals(
                options.Mode,
                "OidcJwt",
                StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authority)
            && authority.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(options.Audience);
    }

    /// <summary>
    /// Parses one deployment-owned CIDR value and fails startup when a trusted
    /// proxy boundary is ambiguous or malformed.
    /// </summary>
    /// <param name="value">The configured network in CIDR notation.</param>
    /// <returns>The parsed framework network value.</returns>
    private static System.Net.IPNetwork ParseKnownNetwork(string value)
    {
        if (!System.Net.IPNetwork.TryParse(value.Trim(), out var network))
        {
            throw new InvalidOperationException(
                "ReverseProxy:KnownNetworks must contain valid CIDR values.");
        }

        return network;
    }

    /// <summary>
    /// Restricts Development role configuration to the same three server policy
    /// roles used in production.
    /// </summary>
    /// <param name="role">The configured role value.</param>
    /// <returns>True for an allow-listed parcel-routing role.</returns>
    private static bool IsAllowedRole(string role)
    {
        return role is AuthorizationPolicies.OperatorRole
            or AuthorizationPolicies.InsuranceApproverRole
            or AuthorizationPolicies.RuleAdministratorRole;
    }

    /// <summary>
    /// Creates one non-queued fixed-window partition whose key is a bounded
    /// authenticated subject or remote-address fallback.
    /// </summary>
    /// <param name="context">The current request used to choose a partition.</param>
    /// <param name="permitLimit">The maximum permits per window.</param>
    /// <param name="windowMinutes">The positive window duration in minutes.</param>
    /// <returns>The configured fixed-window partition.</returns>
    private static RateLimitPartition<string> CreateFixedWindowPartition(
        HttpContext context,
        int permitLimit,
        int windowMinutes)
    {
        string partitionKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        if (partitionKey.Length > 100)
        {
            partitionKey = partitionKey[..100];
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(windowMinutes),
            });
    }

    /// <summary>
    /// Writes a stable Problem Details-shaped rejection with correlation context
    /// and no user-controlled partition data.
    /// </summary>
    /// <param name="context">The rate-limit rejection context.</param>
    /// <param name="cancellationToken">Cancels response writing.</param>
    /// <returns>The asynchronous response operation.</returns>
    private static async ValueTask WriteRateLimitProblemAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Request rate exceeded",
                detail: "Wait before retrying this operation.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "http.rate_limit.exceeded",
                    ["correlationId"] = context.HttpContext.TraceIdentifier,
                })
            .ExecuteAsync(context.HttpContext);
    }
}

/// <summary>
/// Applies the reviewed migration automatically only for explicitly configured
/// Development hosts; production deployment retains external migration control.
/// </summary>
public static class DevelopmentMigrationExtensions
{
    /// <summary>
    /// Applies pending migrations for local review when enabled and rejects that
    /// convenience setting in every non-Development environment.
    /// </summary>
    /// <param name="app">The built web application.</param>
    /// <returns>The asynchronous migration operation.</returns>
    public static async Task ApplyDevelopmentMigrationsAsync(
        this WebApplication app)
    {
        ParcelRoutingDatabaseOptions options = app.Services
            .GetRequiredService<
                Microsoft.Extensions.Options.IOptions<
                    ParcelRoutingDatabaseOptions>>()
            .Value;
        if (!options.ApplyMigrationsOnStartup)
        {
            return;
        }

        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Automatic database migration is allowed only in Development.");
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        ParcelRoutingDbContext context =
            scope.ServiceProvider.GetRequiredService<ParcelRoutingDbContext>();
        await context.Database.MigrateAsync();
    }
}
