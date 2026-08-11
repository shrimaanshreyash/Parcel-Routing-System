using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace ParcelRoutingSystem.Api.IntegrationTests;

/// <summary>
/// Owns a disposable real PostgreSQL instance and a complete API host so
/// HTTP tests prove middleware, authorization, migrations, repositories, and
/// the durable worker together.
/// </summary>
public sealed class ApiIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("parcel_routing_api_tests")
            .WithUsername("parcel_routing_api_test")
            .WithPassword("test-only-password")
            .Build();

    /// <summary>Gets the configured test factory after initialization.</summary>
    public ApiIntegrationFactory Factory { get; private set; } = null!;

    /// <summary>
    /// Starts PostgreSQL and prepares a factory that applies the production
    /// migrations when its first client starts the Development host.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        Factory = new ApiIntegrationFactory(_database.GetConnectionString());
    }

    /// <summary>
    /// Disposes the in-process API host before removing its PostgreSQL dependency.
    /// </summary>
    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}

/// <summary>
/// Configures the real API entry point with isolated test infrastructure and an
/// explicit Development-only reviewer identity.
/// </summary>
public sealed class ApiIntegrationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates the factory with the throwaway database connection.
    /// </summary>
    /// <param name="connectionString">The container-owned PostgreSQL connection.</param>
    public ApiIntegrationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Overrides only external runtime configuration while preserving the real
    /// application pipeline and production service registrations.
    /// </summary>
    /// <param name="builder">The factory web-host builder.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ConnectionString", _connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("ParcelAuthentication:Mode", "Development");
        builder.UseSetting(
            "ParcelAuthentication:DevelopmentAutoAuthenticate",
            "true");
        builder.UseSetting(
            "ParcelAuthentication:DevelopmentActor",
            "api-reviewer");
        builder.UseSetting(
            "ParcelAuthentication:DevelopmentRoles:0",
            "Operator");
        builder.UseSetting(
            "ParcelAuthentication:DevelopmentRoles:1",
            "InsuranceApprover");
        builder.UseSetting(
            "ParcelAuthentication:DevelopmentRoles:2",
            "RuleAdministrator");
        builder.UseSetting("BatchProcessor:Enabled", "true");
        builder.UseSetting("BatchProcessor:IdleDelayMilliseconds", "100");
        builder.UseSetting("RateLimits:RoutingPermitLimit", "1000");
        builder.UseSetting("RateLimits:UploadPermitLimit", "1000");
        builder.UseSetting("RateLimits:ApprovalPermitLimit", "1000");
        builder.UseSetting("RateLimits:QueryPermitLimit", "1000");
    }
}

/// <summary>
/// Serializes API integration tests around one migrated database and worker.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiIntegrationCollection : ICollectionFixture<ApiIntegrationFixture>
{
    /// <summary>Gets the stable collection name.</summary>
    public const string Name = "api-postgresql";
}
