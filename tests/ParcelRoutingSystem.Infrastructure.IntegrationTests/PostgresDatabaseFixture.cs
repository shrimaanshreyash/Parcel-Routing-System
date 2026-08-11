using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ParcelRoutingSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Shares one disposable real PostgreSQL container across serialized integration
/// tests and applies the production migration before any test runs.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("parcel_routing_persistence_tests")
            .WithUsername("parcel_routing_test")
            .WithPassword("test-only-password")
            .Build();

    /// <summary>
    /// Starts PostgreSQL and applies every production migration to prove the
    /// schema can be created from an empty database.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using ParcelRoutingDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Stops and removes the throwaway PostgreSQL container after the test
    /// collection finishes.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a fresh EF context to model a new request or restarted worker
    /// process against the same durable PostgreSQL database.
    /// </summary>
    /// <returns>A new unshared PostgreSQL context.</returns>
    internal ParcelRoutingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ParcelRoutingDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new ParcelRoutingDbContext(options);
    }

    /// <summary>
    /// Removes test-created operational state and rule versions while restoring
    /// the seeded version-one policy as the sole active version.
    /// </summary>
    internal async Task ResetAsync()
    {
        await using ParcelRoutingDbContext context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                insurance_approvals,
                routing_decisions,
                audit_events,
                parcel_batch_rows,
                parcel_batches
            RESTART IDENTITY CASCADE;

            DELETE FROM routing_weight_band_rules WHERE rule_set_version <> 1;
            DELETE FROM routing_insurance_rules WHERE rule_set_version <> 1;
            DELETE FROM routing_rule_sets WHERE version <> 1;
            UPDATE routing_rule_sets
            SET status = 'Active',
                activated_at_utc = TIMESTAMPTZ '2026-07-28 00:00:00+00'
            WHERE version = 1;
            """);
    }
}

/// <summary>
/// Serializes PostgreSQL integration tests around the shared resettable database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresDatabaseCollection :
    ICollectionFixture<PostgresDatabaseFixture>
{
    /// <summary>Gets the collection name used by database test classes.</summary>
    public const string Name = "persistence-postgresql";
}

/// <summary>
/// Supplies a mutable UTC instant so lease-expiry tests advance without sleeping.
/// </summary>
internal sealed class IntegrationTestClock : IApplicationClock
{
    /// <summary>
    /// Creates the test clock at one deterministic UTC instant.
    /// </summary>
    /// <param name="utcNow">The initial test time.</param>
    internal IntegrationTestClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    /// <summary>Gets or sets the deterministic integration-test time.</summary>
    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>
/// Generates deterministic non-empty GUIDs for durable integration-test records.
/// </summary>
internal sealed class IntegrationTestIdentifierGenerator : IIdentifierGenerator
{
    private int _next;

    /// <summary>
    /// Creates the next deterministic unique integration-test identifier.
    /// </summary>
    /// <returns>A non-empty test GUID.</returns>
    public Guid NewId()
    {
        _next++;
        return new Guid($"10000000-0000-0000-0000-{_next:D12}");
    }
}
