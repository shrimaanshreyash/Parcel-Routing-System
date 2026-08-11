using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;
using ParcelRoutingSystem.Infrastructure.Persistence;

namespace ParcelRoutingSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies the real PostgreSQL adapter provides transaction,
/// idempotency, migration, rule-lifecycle, and restart-recovery guarantees.
/// </summary>
[Collection(PostgresDatabaseCollection.Name)]
public sealed class PostgresPersistenceTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresDatabaseFixture _database;

    /// <summary>
    /// Creates one serialized test class around the shared disposable database.
    /// </summary>
    /// <param name="database">The migrated PostgreSQL container fixture.</param>
    public PostgresPersistenceTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// Verifies migrations seed the active default policy and a repeated route
    /// survives a fresh context without duplicating decision or audit rows.
    /// </summary>
    [Fact]
    public async Task Route_WhenContextRestarts_ReplaysDurableDecision()
    {
        await _database.ResetAsync();
        var clock = new IntegrationTestClock(FixedTime);
        var identifiers = new IntegrationTestIdentifierGenerator();
        var command = new RouteParcelCommand(
            "postgres-route-repeat",
            12m,
            1_500m,
            "GB",
            AdditionalAttributes: null,
            OperationMetadata.Create("operator-001", "postgres-route"));
        RouteParcelResult first;

        await using (ParcelRoutingDbContext firstContext = _database.CreateContext())
        {
            var useCase = new RouteParcelUseCase(
                new PostgresRoutingDecisionRepository(firstContext),
                new PostgresRuleSetRepository(firstContext),
                clock,
                identifiers);
            first = await useCase.ExecuteAsync(command);
        }

        await using ParcelRoutingDbContext restartedContext =
            _database.CreateContext();
        var restartedUseCase = new RouteParcelUseCase(
            new PostgresRoutingDecisionRepository(restartedContext),
            new PostgresRuleSetRepository(restartedContext),
            clock,
            identifiers);
        RouteParcelResult replay = await restartedUseCase.ExecuteAsync(command);

        Assert.False(first.WasReplay);
        Assert.True(replay.WasReplay);
        Assert.Equal(first.Decision.Id, replay.Decision.Id);
        Assert.Equal(1, await restartedContext.RoutingDecisions.CountAsync());
        Assert.Equal(1, await restartedContext.AuditEvents.CountAsync());
    }

    /// <summary>
    /// Verifies simultaneous requests with the same idempotency key converge on
    /// one durable decision and one audit event instead of exposing a unique-key
    /// race to either caller.
    /// </summary>
    [Fact]
    public async Task Route_WhenRequestsRace_ConvergesOnOneDurableDecision()
    {
        await _database.ResetAsync();
        var clock = new IntegrationTestClock(FixedTime);
        var command = new RouteParcelCommand(
            "postgres-route-race",
            2m,
            500m,
            "GB",
            AdditionalAttributes: null,
            OperationMetadata.Create("operator-001", "route-race"));

        Task<RouteParcelResult> first = RouteInFreshContextAsync(
            command,
            clock);
        Task<RouteParcelResult> second = RouteInFreshContextAsync(
            command,
            clock);
        RouteParcelResult[] results = await Task.WhenAll(first, second);

        Assert.Single(results.Select(result => result.Decision.Id).Distinct());
        Assert.Single(results, result => !result.WasReplay);
        Assert.Single(results, result => result.WasReplay);

        await using ParcelRoutingDbContext verificationContext =
            _database.CreateContext();
        Assert.Equal(1, await verificationContext.RoutingDecisions.CountAsync());
        Assert.Equal(1, await verificationContext.AuditEvents.CountAsync());
    }

    /// <summary>
    /// Verifies approval remains append-only and idempotent across fresh EF
    /// contexts while the immutable decision remains unchanged.
    /// </summary>
    [Fact]
    public async Task Approval_WhenContextRestarts_ReplaysSingleAppendOnlyRecord()
    {
        await _database.ResetAsync();
        var clock = new IntegrationTestClock(FixedTime);
        var identifiers = new IntegrationTestIdentifierGenerator();
        RoutingDecisionRecord decision;

        await using (ParcelRoutingDbContext routeContext = _database.CreateContext())
        {
            decision = (await new RouteParcelUseCase(
                    new PostgresRoutingDecisionRepository(routeContext),
                    new PostgresRuleSetRepository(routeContext),
                    clock,
                    identifiers)
                .ExecuteAsync(
                    new RouteParcelCommand(
                        "postgres-approval-route",
                        2m,
                        1_500m,
                        "NL",
                        AdditionalAttributes: null,
                        OperationMetadata.Create(
                            "operator-001",
                            "approval-route"))))
                .Decision;
        }

        var command = new ApproveInsuranceCommand(
            decision.Id,
            "postgres-approval-repeat",
            OperationMetadata.Create("approver-001", "approval"));
        InsuranceApprovalRecord first;
        await using (ParcelRoutingDbContext approvalContext =
            _database.CreateContext())
        {
            first = await new ApproveInsuranceUseCase(
                    new PostgresInsuranceApprovalRepository(approvalContext),
                    clock,
                    identifiers)
                .ExecuteAsync(command);
        }

        await using ParcelRoutingDbContext restartedContext =
            _database.CreateContext();
        InsuranceApprovalRecord replay = await new ApproveInsuranceUseCase(
                new PostgresInsuranceApprovalRepository(restartedContext),
                clock,
                identifiers)
            .ExecuteAsync(command);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, await restartedContext.InsuranceApprovals.CountAsync());
        Assert.Equal(2, await restartedContext.AuditEvents.CountAsync());
        Assert.Equal(
            ApprovalState.PendingInsuranceApproval,
            (await restartedContext.RoutingDecisions.SingleAsync()).ApprovalState);
    }

    /// <summary>
    /// Verifies an abandoned lease is reclaimed by a new context after expiry and
    /// completes exactly one decision with an incremented attempt count.
    /// </summary>
    [Fact]
    public async Task Batch_WhenWorkerRestartsAfterLeaseExpiry_RecoversRow()
    {
        await _database.ResetAsync();
        var clock = new IntegrationTestClock(FixedTime);
        var identifiers = new IntegrationTestIdentifierGenerator();
        BatchWriteResult created;

        await using (ParcelRoutingDbContext createContext = _database.CreateContext())
        {
            created = await new CreateBatchUseCase(
                    new PostgresBatchRepository(createContext),
                    clock,
                    identifiers)
                .ExecuteAsync(
                    new CreateBatchCommand(
                        "postgres-batch-restart",
                        "GB",
                        [new BatchParcelRowInput(15m, 1_500m)],
                        OperationMetadata.Create(
                            "operator-001",
                            "batch-create")));
        }

        await using (ParcelRoutingDbContext abandonedContext =
            _database.CreateContext())
        {
            BatchRowClaim abandoned = (await new PostgresBatchRepository(
                    abandonedContext)
                .ClaimNextAsync(
                    clock.UtcNow,
                    TimeSpan.FromMinutes(2),
                    CancellationToken.None))!;
            Assert.Equal(1, abandoned.Row.AttemptCount);
        }

        clock.UtcNow = clock.UtcNow.AddMinutes(3);
        await using (ParcelRoutingDbContext restartedContext =
            _database.CreateContext())
        {
            BatchRowProcessResult result = await new ProcessNextBatchRowUseCase(
                    new PostgresBatchRepository(restartedContext),
                    new PostgresRuleSetRepository(restartedContext),
                    clock,
                    identifiers)
                .ExecuteAsync("worker-restarted", "batch-recovery");
            BatchRecord stored = (await new PostgresBatchRepository(
                    restartedContext)
                .GetBatchAsync(created.Batch.Id, CancellationToken.None))!;

            Assert.Equal(BatchRowProcessStatus.Completed, result.Status);
            Assert.Equal(BatchStatus.Completed, stored.Status);
            Assert.Equal(2, stored.Rows.Single().AttemptCount);
            Assert.Equal(1, await restartedContext.RoutingDecisions.CountAsync());
        }
    }

    /// <summary>
    /// Verifies draft, activation, and rollback each survive a new context while
    /// the database retains exactly one active version.
    /// </summary>
    [Fact]
    public async Task RuleLifecycle_WhenContextsRestart_PreservesAtomicActiveVersion()
    {
        await _database.ResetAsync();
        var clock = new IntegrationTestClock(FixedTime);
        var identifiers = new IntegrationTestIdentifierGenerator();
        RuleSetDefinition versionTwo = CreateVersionTwo();

        await using (ParcelRoutingDbContext firstContext = _database.CreateContext())
        {
            var lifecycle = new RuleSetLifecycleUseCase(
                new PostgresRuleSetRepository(firstContext),
                clock,
                identifiers);
            await lifecycle.CreateDraftAsync(
                versionTwo,
                "postgres-rule-draft",
                OperationMetadata.Create("admin-001", "rule-draft"));
            await lifecycle.ActivateAsync(
                2,
                "postgres-rule-activate",
                OperationMetadata.Create("admin-001", "rule-activate"));
        }

        await using (ParcelRoutingDbContext secondContext = _database.CreateContext())
        {
            StoredRuleSet active = (await new PostgresRuleSetRepository(
                    secondContext)
                .GetActiveAsync(CancellationToken.None))!;
            Assert.Equal(2, active.Definition.Version);

            await new RuleSetLifecycleUseCase(
                    new PostgresRuleSetRepository(secondContext),
                    clock,
                    identifiers)
                .RollbackAsync(
                    1,
                    "postgres-rule-rollback",
                    OperationMetadata.Create("admin-001", "rule-rollback"));
        }

        await using ParcelRoutingDbContext verificationContext =
            _database.CreateContext();
        StoredRuleSet restored = (await new PostgresRuleSetRepository(
                verificationContext)
            .GetActiveAsync(CancellationToken.None))!;

        Assert.Equal(1, restored.Definition.Version);
        Assert.Equal(
            1,
            await verificationContext.RuleSets.CountAsync(
                item => item.Status == RuleSetLifecycleStatus.Active));
        Assert.Equal(3, await verificationContext.AuditEvents.CountAsync());
    }

    /// <summary>
    /// Verifies a database failure while inserting the audit event rolls back the
    /// proposed decision instead of leaving unaudited business state.
    /// </summary>
    [Fact]
    public async Task Decision_WhenAuditInsertFails_RollsBackWholeTransaction()
    {
        await _database.ResetAsync();
        Guid duplicateAuditId = Guid.Parse(
            "20000000-0000-0000-0000-000000000001");
        await using (ParcelRoutingDbContext firstContext = _database.CreateContext())
        {
            var repository = new PostgresRoutingDecisionRepository(firstContext);
            await repository.SaveAsync(
                CreateDecision(
                    Guid.Parse("30000000-0000-0000-0000-000000000001"),
                    "transaction-first"),
                CreateAudit(duplicateAuditId, "transaction-first"),
                CancellationToken.None);
        }

        await using (ParcelRoutingDbContext failingContext =
            _database.CreateContext())
        {
            var repository = new PostgresRoutingDecisionRepository(failingContext);
            await Assert.ThrowsAsync<DbUpdateException>(
                () => repository.SaveAsync(
                    CreateDecision(
                        Guid.Parse("30000000-0000-0000-0000-000000000002"),
                        "transaction-should-rollback"),
                    CreateAudit(
                        duplicateAuditId,
                        "transaction-should-rollback"),
                    CancellationToken.None));
        }

        await using ParcelRoutingDbContext verificationContext =
            _database.CreateContext();
        Assert.Equal(1, await verificationContext.RoutingDecisions.CountAsync());
        Assert.Null(
            await verificationContext.RoutingDecisions.SingleOrDefaultAsync(
                item => item.IdempotencyKey == "transaction-should-rollback"));
    }

    /// <summary>
    /// Creates a valid version-two policy with a deliberately wider Mail band.
    /// </summary>
    /// <returns>A semantically valid constrained rule-set definition.</returns>
    private static RuleSetDefinition CreateVersionTwo()
    {
        return new RuleSetDefinition(
            2,
            [
                new WeightBandDefinition(
                    "MAIL-UP-TO-2-KG",
                    100,
                    0m,
                    2m,
                    RoutingDepartment.Mail),
                new WeightBandDefinition(
                    "REGULAR-UP-TO-10-KG",
                    200,
                    2m,
                    10m,
                    RoutingDepartment.Regular),
                new WeightBandDefinition(
                    "HEAVY-OVER-10-KG",
                    300,
                    10m,
                    null,
                    RoutingDepartment.Heavy),
            ],
            new InsuranceRuleDefinition(
                "INSURANCE-OVER-1000-EUR",
                1_000,
                1_000m));
    }

    /// <summary>
    /// Creates one valid immutable decision for transaction rollback setup.
    /// </summary>
    /// <param name="id">The server-owned decision identifier.</param>
    /// <param name="idempotencyKey">The unique routing replay key.</param>
    /// <returns>A valid version-one decision record.</returns>
    private static RoutingDecisionRecord CreateDecision(
        Guid id,
        string idempotencyKey)
    {
        return new RoutingDecisionRecord(
            id,
            idempotencyKey,
            new string('A', 64),
            2m,
            10m,
            "GB",
            RoutingDepartment.Regular,
            ApprovalState.NotRequired,
            1,
            [DefaultRoutingRuleIds.RegularWeight.Value],
            ["Weight 2 kg routes to Regular Department."],
            FixedTime,
            "transaction-test",
            BatchId: null,
            BatchRowId: null);
    }

    /// <summary>
    /// Creates one privacy-safe audit event for transaction rollback setup.
    /// </summary>
    /// <param name="id">The audit identifier, intentionally reusable by a failure test.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <returns>A valid decision-created audit record.</returns>
    private static AuditEventRecord CreateAudit(
        Guid id,
        string idempotencyKey)
    {
        return AuditEventRecord.Create(
            id,
            "routing.decision-created",
            "routing-decision",
            id.ToString("D"),
            OperationMetadata.Create("operator-001", "transaction-test"),
            idempotencyKey,
            FixedTime);
    }

    /// <summary>
    /// Routes through an isolated context so concurrent idempotency tests model
    /// separate requests and separate scoped EF units of work.
    /// </summary>
    /// <param name="command">The shared idempotent routing request.</param>
    /// <param name="clock">The deterministic operation clock.</param>
    /// <returns>The durable routing result from this request scope.</returns>
    private async Task<RouteParcelResult> RouteInFreshContextAsync(
        RouteParcelCommand command,
        IApplicationClock clock)
    {
        await using ParcelRoutingDbContext context = _database.CreateContext();
        return await new RouteParcelUseCase(
                new PostgresRoutingDecisionRepository(context),
                new PostgresRuleSetRepository(context),
                clock,
                new ParcelRoutingSystem.Infrastructure.GuidIdentifierGenerator())
            .ExecuteAsync(command);
    }
}
