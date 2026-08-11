using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Domain;
using ParcelRoutingSystem.Domain.Parcels;

namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Validates and durably accepts one parsed batch while isolating invalid rows
/// instead of discarding valid work.
/// </summary>
public sealed class CreateBatchUseCase
{
    private const int MaximumRows = 10_000;

    private readonly IBatchRepository _repository;
    private readonly IApplicationClock _clock;
    private readonly IIdentifierGenerator _identifiers;

    /// <summary>
    /// Creates the batch-acceptance coordinator with transactional persistence
    /// and server-owned time and identifiers.
    /// </summary>
    /// <param name="repository">The durable transactional batch repository.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    /// <param name="identifiers">The server-owned record identifier generator.</param>
    public CreateBatchUseCase(
        IBatchRepository repository,
        IApplicationClock clock,
        IIdentifierGenerator identifiers)
    {
        _repository = repository;
        _clock = clock;
        _identifiers = identifiers;
    }

    /// <summary>
    /// Persists a bounded batch and marks invalid rows independently so later
    /// processors can continue every valid row.
    /// </summary>
    /// <param name="command">The parsed rows, explicit country, and operation metadata.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable batch and replay status.</returns>
    public async Task<BatchWriteResult> ExecuteAsync(
        CreateBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Rows);
        ArgumentNullException.ThrowIfNull(command.Metadata);

        if (command.Rows.Count is < 1 or > MaximumRows)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.BatchInvalid,
                $"A batch must contain between 1 and {MaximumRows} rows.");
        }

        string idempotencyKey = ApplicationGuard.RequiredText(
            command.IdempotencyKey,
            100,
            ApplicationErrorCodes.IdempotencyKeyInvalid,
            "Idempotency key");
        CountryCode? fallbackCountry = string.IsNullOrWhiteSpace(
            command.FallbackDestinationCountry)
            ? null
            : CountryCode.FromAlpha2(command.FallbackDestinationCountry);
        EnsureEveryRowHasCountry(command.Rows, fallbackCountry);
        string requestFingerprint = ApplicationRequestFingerprint.ForBatch(
            fallbackCountry,
            command.Rows);
        BatchRecord? replay = await _repository.FindBatchByIdempotencyKeyAsync(
            idempotencyKey,
            cancellationToken);
        if (replay is not null)
        {
            EnsureMatchingFingerprint(
                replay.RequestFingerprint,
                requestFingerprint);
            return new BatchWriteResult(replay, WasCreated: false);
        }

        BatchRecord? previous = await _repository.FindLatestByFingerprintAsync(
            requestFingerprint,
            cancellationToken);
        if (previous is not null && !command.AllowDuplicate)
        {
            throw new DuplicateManifestException(
                previous.Id,
                previous.CreatedAtUtc);
        }

        Guid batchId = _identifiers.NewId();
        DateTimeOffset createdAtUtc = _clock.UtcNow.ToUniversalTime();
        var rows = new List<BatchRowRecord>(command.Rows.Count);

        for (int index = 0; index < command.Rows.Count; index++)
        {
            BatchParcelRowInput input = command.Rows[index];
            rows.Add(CreateRow(batchId, index + 1, input, fallbackCountry));
        }

        int failedRows = rows.Count(row => row.Status == BatchRowStatus.ValidationFailed);
        BatchStatus status = failedRows == rows.Count
            ? BatchStatus.CompletedWithErrors
            : BatchStatus.Pending;
        var batch = new BatchRecord(
            batchId,
            idempotencyKey,
            requestFingerprint,
            fallbackCountry?.Value,
            status,
            rows.Count,
            CompletedRows: 0,
            failedRows,
            createdAtUtc,
            command.Metadata.ActorId,
            rows.AsReadOnly());
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "batch.created",
            "batch",
            batchId.ToString("D"),
            command.Metadata,
            idempotencyKey,
            createdAtUtc,
            new Dictionary<string, string>
            {
                ["totalRows"] = batch.TotalRows.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["validationFailedRows"] = batch.FailedRows.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            });

        return await SaveAndVerifyAsync(
            batch,
            auditEvent,
            cancellationToken);
    }

    /// <summary>
    /// Persists or replays the batch and rejects reuse of the key for a different
    /// normalized manifest.
    /// </summary>
    /// <param name="batch">The proposed batch and request fingerprint.</param>
    /// <param name="auditEvent">The creation audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The new or matching replay result.</returns>
    private async Task<BatchWriteResult> SaveAndVerifyAsync(
        BatchRecord batch,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        BatchWriteResult result = await _repository.SaveBatchAsync(
            batch,
            auditEvent,
            cancellationToken);
        if (!string.Equals(
                result.Batch.RequestFingerprint,
                batch.RequestFingerprint,
                StringComparison.Ordinal))
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for a different batch.");
        }

        return result;
    }

    /// <summary>
    /// Rejects reuse of an operation key for changed normalized content while
    /// allowing the original response to be replayed safely.
    /// </summary>
    /// <param name="stored">The fingerprint already bound to the operation.</param>
    /// <param name="requested">The fingerprint calculated for this request.</param>
    private static void EnsureMatchingFingerprint(
        string stored,
        string requested)
    {
        if (!string.Equals(stored, requested, StringComparison.Ordinal))
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.IdempotencyConflict,
                "The idempotency key was already used for a different batch.");
        }
    }

    /// <summary>
    /// Creates one durable row and converts domain validation failures into a
    /// safe isolated row status rather than rejecting the full batch.
    /// </summary>
    /// <param name="batchId">The server-owned parent batch identifier.</param>
    /// <param name="rowNumber">The stable one-based source position.</param>
    /// <param name="input">The privacy-minimized parsed row facts.</param>
    /// <param name="fallbackCountry">The optional validated manifest fallback.</param>
    /// <returns>A pending valid row or an isolated validation-failed row.</returns>
    private BatchRowRecord CreateRow(
        Guid batchId,
        int rowNumber,
        BatchParcelRowInput input,
        CountryCode? fallbackCountry)
    {
        Guid rowId = _identifiers.NewId();
        if (!string.IsNullOrWhiteSpace(input.ValidationErrorCode))
        {
            return CreateValidationFailure(
                rowId,
                batchId,
                rowNumber,
                input,
                fallbackCountry,
                input.ValidationErrorCode,
                input.ValidationErrorMessage
                    ?? "The manifest row contains invalid routing facts.");
        }

        try
        {
            (CountryCode country, BatchCountrySource countrySource) =
                ResolveCountry(input, fallbackCountry);
            _ = Parcel.Create(
                Weight.FromKilograms(input.WeightKilograms),
                DeclaredValue.FromEuros(input.DeclaredValueEuros),
                country);

            return new BatchRowRecord(
                rowId,
                batchId,
                rowNumber,
                input.WeightKilograms,
                input.DeclaredValueEuros,
                country.Value,
                countrySource,
                BatchRowStatus.Pending,
                ErrorCode: null,
                ErrorMessage: null,
                AttemptCount: 0,
                DecisionId: null);
        }
        catch (DomainValidationException exception)
        {
            return CreateValidationFailure(
                rowId,
                batchId,
                rowNumber,
                input,
                fallbackCountry,
                exception.Code,
                exception.OperatorMessage);
        }
    }

    /// <summary>
    /// Creates one durable failed-row record with a safe country placeholder so
    /// malformed parcel facts never discard valid siblings or leak raw input.
    /// </summary>
    /// <param name="rowId">The server-owned row identifier.</param>
    /// <param name="batchId">The server-owned parent batch identifier.</param>
    /// <param name="rowNumber">The stable one-based source position.</param>
    /// <param name="input">The privacy-minimized parsed row facts.</param>
    /// <param name="fallbackCountry">The optional validated manifest fallback.</param>
    /// <param name="errorCode">The stable parser or domain failure code.</param>
    /// <param name="errorMessage">The safe operator-facing failure explanation.</param>
    /// <returns>A validation-failed row that requires no worker claim.</returns>
    private static BatchRowRecord CreateValidationFailure(
        Guid rowId,
        Guid batchId,
        int rowNumber,
        BatchParcelRowInput input,
        CountryCode? fallbackCountry,
        string errorCode,
        string errorMessage)
    {
        (string country, BatchCountrySource countrySource) =
            ResolveFailedCountry(input, fallbackCountry);
        return new BatchRowRecord(
            rowId,
            batchId,
            rowNumber,
            input.WeightKilograms,
            input.DeclaredValueEuros,
            country,
            countrySource,
            BatchRowStatus.ValidationFailed,
            errorCode,
            errorMessage,
            AttemptCount: 0,
            DecisionId: null);
    }

    /// <summary>
    /// Retains valid country provenance for a failed row and otherwise returns
    /// a non-country marker that cannot be mistaken for a supported ISO code.
    /// </summary>
    /// <param name="input">The failed privacy-minimized row.</param>
    /// <param name="fallbackCountry">The optional validated manifest fallback.</param>
    /// <returns>A two-character safe value and its provenance state.</returns>
    private static (string Country, BatchCountrySource Source) ResolveFailedCountry(
        BatchParcelRowInput input,
        CountryCode? fallbackCountry)
    {
        if (!string.IsNullOrWhiteSpace(input.DestinationCountry))
        {
            try
            {
                return (
                    CountryCode.FromAlpha2(input.DestinationCountry).Value,
                    BatchCountrySource.Row);
            }
            catch (DomainValidationException)
            {
                return ("--", BatchCountrySource.Unavailable);
            }
        }

        return fallbackCountry is null
            ? ("--", BatchCountrySource.Unavailable)
            : (fallbackCountry.Value.Value, BatchCountrySource.ManifestFallback);
    }

    /// <summary>
    /// Rejects the complete request before persistence when any row lacks both
    /// an explicit row country and an operator-supplied fallback.
    /// </summary>
    /// <param name="rows">The ordered parsed manifest rows.</param>
    /// <param name="fallbackCountry">The optional validated manifest fallback.</param>
    private static void EnsureEveryRowHasCountry(
        IReadOnlyList<BatchParcelRowInput> rows,
        CountryCode? fallbackCountry)
    {
        if (fallbackCountry is not null)
        {
            return;
        }

        bool hasMissingCountry = rows.Any(
            row => string.IsNullOrWhiteSpace(row.ValidationErrorCode)
                && string.IsNullOrWhiteSpace(row.DestinationCountry));
        if (hasMissingCountry)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.BatchInvalid,
                "A fallback destination country is required when any manifest row omits country.");
        }
    }

    /// <summary>
    /// Selects and validates the row-level country when present, otherwise
    /// applies the explicit fallback and records that provenance.
    /// </summary>
    /// <param name="input">The parsed privacy-minimized row.</param>
    /// <param name="fallbackCountry">The optional validated manifest fallback.</param>
    /// <returns>The validated country and durable provenance classification.</returns>
    private static (CountryCode Country, BatchCountrySource Source) ResolveCountry(
        BatchParcelRowInput input,
        CountryCode? fallbackCountry)
    {
        if (!string.IsNullOrWhiteSpace(input.DestinationCountry))
        {
            return (
                CountryCode.FromAlpha2(input.DestinationCountry),
                BatchCountrySource.Row);
        }

        return (
            fallbackCountry
                ?? throw new ApplicationOperationException(
                    ApplicationErrorCodes.BatchInvalid,
                    "A destination country is required for every batch row."),
            BatchCountrySource.ManifestFallback);
    }
}
