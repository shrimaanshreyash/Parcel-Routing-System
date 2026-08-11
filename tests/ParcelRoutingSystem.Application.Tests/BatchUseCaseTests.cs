using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Verifies batch acceptance isolates invalid rows and durable row processing
/// produces idempotent restart-safe decisions.
/// </summary>
public sealed class BatchUseCaseTests
{
    /// <summary>
    /// Verifies one malformed row is persisted as failed while valid rows remain
    /// available for processing.
    /// </summary>
    [Fact]
    public async Task Create_WhenOneRowIsInvalid_IsolatesFailure()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-001",
            "NL",
            [
                new BatchParcelRowInput(2m, 10m),
                new BatchParcelRowInput(0m, 10m),
                new BatchParcelRowInput(15m, 1_500m),
            ],
            ApplicationTestFixture.Metadata());

        BatchWriteResult result = await useCase.ExecuteAsync(command);

        Assert.True(result.WasCreated);
        Assert.Equal(3, result.Batch.TotalRows);
        Assert.Equal(1, result.Batch.FailedRows);
        Assert.Equal(2, result.Batch.Rows.Count(
            row => row.Status == BatchRowStatus.Pending));
        Assert.Equal(
            BatchRowStatus.ValidationFailed,
            result.Batch.Rows[1].Status);
    }

    /// <summary>
    /// Verifies domain parameter metadata is not appended to the durable
    /// operator explanation stored for an invalid declared value.
    /// </summary>
    [Fact]
    public async Task Create_WhenDeclaredValueIsNegative_PreservesPlainSafeMessage()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());

        BatchWriteResult result = await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-negative-value-message",
                "NL",
                [new BatchParcelRowInput(2m, -1m)],
                ApplicationTestFixture.Metadata()));

        BatchRowRecord failure = Assert.Single(result.Batch.Rows);
        Assert.Equal(BatchRowStatus.ValidationFailed, failure.Status);
        Assert.Equal(
            "Declared parcel value cannot be negative.",
            failure.ErrorMessage);
        Assert.DoesNotContain("Parameter", failure.ErrorMessage);
    }

    /// <summary>
    /// Verifies one unsupported ISO code becomes a row failure while valid
    /// country rows in the same manifest remain pending for evaluation.
    /// </summary>
    [Fact]
    public async Task Create_WhenOneRowCountryIsInvalid_IsolatesFailure()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-invalid-row-country",
            FallbackDestinationCountry: null,
            [
                new BatchParcelRowInput(1m, 100m, "GB"),
                new BatchParcelRowInput(2m, 200m, "ZZ"),
                new BatchParcelRowInput(11m, 1_200m, "NL"),
            ],
            ApplicationTestFixture.Metadata());

        BatchWriteResult result = await useCase.ExecuteAsync(command);

        Assert.Equal(1, result.Batch.FailedRows);
        Assert.Equal(
            2,
            result.Batch.Rows.Count(row => row.Status == BatchRowStatus.Pending));
        BatchRowRecord failure = result.Batch.Rows[1];
        Assert.Equal(BatchRowStatus.ValidationFailed, failure.Status);
        Assert.Equal(
            ParcelRoutingSystem.Domain.DomainErrorCodes.CountryInvalid,
            failure.ErrorCode);
        Assert.Equal("--", failure.DestinationCountry);
        Assert.Equal(BatchCountrySource.Unavailable, failure.CountrySource);
    }

    /// <summary>
    /// Verifies a parser-classified structural failure is persisted without
    /// asking the domain to interpret placeholder numeric values.
    /// </summary>
    [Fact]
    public async Task Create_WhenParserClassifiesOneRowInvalid_PreservesSafeFailure()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-parser-row-failure",
            "GB",
            [
                new BatchParcelRowInput(2m, 10m),
                new BatchParcelRowInput(
                    0m,
                    0m,
                    ValidationErrorCode: ApplicationErrorCodes.ManifestRowInvalid,
                    ValidationErrorMessage: "Each Parcel must contain one valid Weight value."),
            ],
            ApplicationTestFixture.Metadata());

        BatchWriteResult result = await useCase.ExecuteAsync(command);

        Assert.Equal(1, result.Batch.FailedRows);
        BatchRowRecord failure = result.Batch.Rows[1];
        Assert.Equal(BatchRowStatus.ValidationFailed, failure.Status);
        Assert.Equal(ApplicationErrorCodes.ManifestRowInvalid, failure.ErrorCode);
        Assert.Equal(
            "Each Parcel must contain one valid Weight value.",
            failure.ErrorMessage);
        Assert.Equal("GB", failure.DestinationCountry);
        Assert.Equal(BatchCountrySource.ManifestFallback, failure.CountrySource);
    }

    /// <summary>
    /// Verifies an accepted valid row can be claimed, routed, and committed with
    /// its parent batch counters in one repository operation.
    /// </summary>
    [Fact]
    public async Task Process_WhenPendingRowExists_CompletesDecisionAndBatch()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var identifiers = new SequenceIdentifierGenerator();
        BatchWriteResult created = await new CreateBatchUseCase(
                store,
                clock,
                identifiers)
            .ExecuteAsync(
                new CreateBatchCommand(
                    "batch-process",
                    "GB",
                    [new BatchParcelRowInput(15m, 1_500m)],
                    ApplicationTestFixture.Metadata()));
        var processor = new ProcessNextBatchRowUseCase(
            store,
            store,
            clock,
            identifiers);

        BatchRowProcessResult result = await processor.ExecuteAsync(
            "worker-001",
            "batch-loop");
        BatchRecord stored = (await store.GetBatchAsync(
            created.Batch.Id,
            CancellationToken.None))!;

        Assert.Equal(BatchRowProcessStatus.Completed, result.Status);
        Assert.Equal(BatchStatus.Completed, stored.Status);
        Assert.Equal(1, stored.CompletedRows);
        Assert.Equal(
            ApprovalState.PendingInsuranceApproval,
            store.Decisions.Values.Single().ApprovalState);
    }

    /// <summary>
    /// Verifies an abandoned processing lease becomes claimable after expiry,
    /// modelling safe work recovery by a newly started processor.
    /// </summary>
    [Fact]
    public async Task Claim_WhenPreviousLeaseExpires_RecoversRowAfterRestart()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var identifiers = new SequenceIdentifierGenerator();
        BatchWriteResult created = await new CreateBatchUseCase(
                store,
                clock,
                identifiers)
            .ExecuteAsync(
                new CreateBatchCommand(
                    "batch-restart",
                    "GB",
                    [new BatchParcelRowInput(2m, 10m)],
                    ApplicationTestFixture.Metadata()));

        BatchRowClaim first = (await store.ClaimNextAsync(
            clock.UtcNow,
            TimeSpan.FromMinutes(2),
            CancellationToken.None))!;
        clock.UtcNow = clock.UtcNow.AddMinutes(3);
        BatchRowClaim recovered = (await store.ClaimNextAsync(
            clock.UtcNow,
            TimeSpan.FromMinutes(2),
            CancellationToken.None))!;

        Assert.Equal(first.Row.Id, recovered.Row.Id);
        Assert.NotEqual(first.ClaimToken, recovered.ClaimToken);
        Assert.Equal(2, recovered.Row.AttemptCount);
        Assert.Equal(created.Batch.Id, recovered.Row.BatchId);
    }

    /// <summary>
    /// Verifies a temporary absence of an active policy returns the row to
    /// pending state instead of permanently failing recoverable work.
    /// </summary>
    [Fact]
    public async Task Process_WhenActiveRuleSetIsUnavailable_DefersRowForRetry()
    {
        var store = new InMemoryApplicationStore();
        var clock = new MutableClock(ApplicationTestFixture.FixedTime);
        var identifiers = new SequenceIdentifierGenerator();
        BatchWriteResult created = await new CreateBatchUseCase(
                store,
                clock,
                identifiers)
            .ExecuteAsync(
                new CreateBatchCommand(
                    "batch-policy-unavailable",
                    "GB",
                    [new BatchParcelRowInput(2m, 10m)],
                    ApplicationTestFixture.Metadata()));
        var processor = new ProcessNextBatchRowUseCase(
            store,
            store,
            clock,
            identifiers);

        BatchRowProcessResult result = await processor.ExecuteAsync(
            "worker-001",
            "policy-unavailable");
        BatchRecord stored = (await store.GetBatchAsync(
            created.Batch.Id,
            CancellationToken.None))!;

        Assert.Equal(BatchRowProcessStatus.Deferred, result.Status);
        Assert.Equal(BatchStatus.Pending, stored.Status);
        Assert.Equal(BatchRowStatus.Pending, stored.Rows.Single().Status);
        Assert.Equal(0, stored.FailedRows);
    }

    /// <summary>
    /// Verifies repeating batch creation returns the original durable graph and
    /// does not append another creation audit event.
    /// </summary>
    [Fact]
    public async Task Create_WhenIdempotencyKeyRepeats_ReturnsOriginalBatch()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-repeat",
            "GB",
            [new BatchParcelRowInput(2m, 10m)],
            ApplicationTestFixture.Metadata());

        BatchWriteResult first = await useCase.ExecuteAsync(command);
        BatchWriteResult second = await useCase.ExecuteAsync(command);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.Batch.Id, second.Batch.Id);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies a new operation with the same normalized manifest is stopped
    /// with safe prior-batch evidence until an operator confirms it.
    /// </summary>
    [Fact]
    public async Task Create_WhenManifestWasImportedPreviously_RequiresConfirmation()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        BatchWriteResult first = await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-original",
                "GB",
                [new BatchParcelRowInput(2m, 10m)],
                ApplicationTestFixture.Metadata()));

        DuplicateManifestException exception =
            await Assert.ThrowsAsync<DuplicateManifestException>(
                () => useCase.ExecuteAsync(
                    new CreateBatchCommand(
                        "batch-second-operation",
                        "GB",
                        [new BatchParcelRowInput(2m, 10m)],
                        ApplicationTestFixture.Metadata())));

        Assert.Equal(first.Batch.Id, exception.PreviousBatchId);
        Assert.Equal(first.Batch.CreatedAtUtc, exception.PreviousImportedAtUtc);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies explicit confirmation creates a new batch while duplicate rows
    /// inside that manifest remain separate source positions.
    /// </summary>
    [Fact]
    public async Task Create_WhenDuplicateIsConfirmed_CreatesNewCompleteManifest()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        BatchParcelRowInput[] rows =
        [
            new BatchParcelRowInput(2m, 10m),
            new BatchParcelRowInput(2m, 10m),
        ];
        BatchWriteResult first = await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-original-duplicates",
                "GB",
                rows,
                ApplicationTestFixture.Metadata()));
        BatchWriteResult confirmed = await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-confirmed-duplicates",
                "GB",
                rows,
                ApplicationTestFixture.Metadata(),
                AllowDuplicate: true));

        Assert.True(confirmed.WasCreated);
        Assert.NotEqual(first.Batch.Id, confirmed.Batch.Id);
        Assert.Equal(2, confirmed.Batch.Rows.Count);
        Assert.Equal([1, 2], confirmed.Batch.Rows.Select(row => row.RowNumber));
        Assert.Equal(2, store.AuditEvents.Count);
    }

    /// <summary>
    /// Verifies a batch replay key is bound to the complete normalized manifest
    /// and cannot be reused for changed row facts.
    /// </summary>
    [Fact]
    public async Task Create_WhenKeyIsReusedForDifferentRows_RejectsConflict()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-conflict",
                "GB",
                [new BatchParcelRowInput(2m, 10m)],
                ApplicationTestFixture.Metadata()));

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(
                    new CreateBatchCommand(
                        "batch-conflict",
                        "GB",
                        [new BatchParcelRowInput(12m, 10m)],
                        ApplicationTestFixture.Metadata())));

        Assert.Equal(
            ApplicationErrorCodes.IdempotencyConflict,
            exception.Code);
        Assert.Single(store.AuditEvents);
    }

    /// <summary>
    /// Verifies an explicit row country takes precedence over the manifest
    /// fallback and records that decision as durable provenance.
    /// </summary>
    [Fact]
    public async Task Create_WhenRowProvidesCountry_PreservesRowProvenance()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-row-country",
            "GB",
            [new BatchParcelRowInput(2m, 10m, "NL")],
            ApplicationTestFixture.Metadata());

        BatchWriteResult result = await useCase.ExecuteAsync(command);

        BatchRowRecord row = Assert.Single(result.Batch.Rows);
        Assert.Equal("NL", row.DestinationCountry);
        Assert.Equal(BatchCountrySource.Row, row.CountrySource);
        Assert.Equal("GB", result.Batch.FallbackDestinationCountry);
    }

    /// <summary>
    /// Verifies the complete import is rejected before persistence when a row
    /// has no country and the operator supplies no fallback.
    /// </summary>
    [Fact]
    public async Task Create_WhenCountryIsMissingEverywhere_RejectsBatch()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        var command = new CreateBatchCommand(
            "batch-country-missing",
            FallbackDestinationCountry: null,
            [new BatchParcelRowInput(2m, 10m)],
            ApplicationTestFixture.Metadata());

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(command));

        Assert.Equal(ApplicationErrorCodes.BatchInvalid, exception.Code);
        Assert.Empty(store.AuditEvents);
    }

    /// <summary>
    /// Verifies country is part of the idempotency fingerprint so one key cannot
    /// silently replay a manifest whose destination facts changed.
    /// </summary>
    [Fact]
    public async Task Create_WhenRowCountryChangesForSameKey_RejectsConflict()
    {
        InMemoryApplicationStore store = ApplicationTestFixture.CreateStore();
        var useCase = new CreateBatchUseCase(
            store,
            new MutableClock(ApplicationTestFixture.FixedTime),
            new SequenceIdentifierGenerator());
        await useCase.ExecuteAsync(
            new CreateBatchCommand(
                "batch-country-conflict",
                "GB",
                [new BatchParcelRowInput(2m, 10m, "NL")],
                ApplicationTestFixture.Metadata()));

        ApplicationOperationException exception =
            await Assert.ThrowsAsync<ApplicationOperationException>(
                () => useCase.ExecuteAsync(
                    new CreateBatchCommand(
                        "batch-country-conflict",
                        "GB",
                        [new BatchParcelRowInput(2m, 10m, "DE")],
                        ApplicationTestFixture.Metadata())));

        Assert.Equal(ApplicationErrorCodes.IdempotencyConflict, exception.Code);
    }
}
