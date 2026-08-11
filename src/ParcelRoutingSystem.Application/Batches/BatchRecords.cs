namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Identifies the durable lifecycle state of a privacy-minimized parcel batch.
/// </summary>
public enum BatchStatus
{
    /// <summary>The batch contains rows waiting to be claimed.</summary>
    Pending = 1,

    /// <summary>At least one row is being processed or remains pending.</summary>
    Processing = 2,

    /// <summary>Every row completed with a routing decision.</summary>
    Completed = 3,

    /// <summary>Every row finished, with one or more isolated failures.</summary>
    CompletedWithErrors = 4,
}

/// <summary>
/// Identifies the durable state of one independently retryable batch row.
/// </summary>
public enum BatchRowStatus
{
    /// <summary>The row is valid and waiting for a processor claim.</summary>
    Pending = 1,

    /// <summary>The row is held by a time-bounded processor lease.</summary>
    Processing = 2,

    /// <summary>The row has one immutable routing decision.</summary>
    Completed = 3,

    /// <summary>The row contained permanently invalid parcel facts.</summary>
    ValidationFailed = 4,

    /// <summary>The row failed permanently during processing.</summary>
    ProcessingFailed = 5,
}

/// <summary>
/// Records whether an XML row supplied its own country or used explicit
/// manifest-level metadata so country provenance remains explainable.
/// </summary>
public enum BatchCountrySource
{
    /// <summary>The supported manifest row contained the country directly.</summary>
    Row = 1,

    /// <summary>The operator supplied one fallback for a row that omitted country.</summary>
    ManifestFallback = 2,

    /// <summary>The failed row did not contain a valid country fact.</summary>
    Unavailable = 3,
}

/// <summary>
/// Captures the durable non-personal facts and status of one batch row.
/// </summary>
public sealed record BatchRowRecord(
    Guid Id,
    Guid BatchId,
    int RowNumber,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    BatchCountrySource CountrySource,
    BatchRowStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    int AttemptCount,
    Guid? DecisionId);

/// <summary>
/// Captures batch-level identity, replay metadata, progress counters, and
/// privacy-safe row records.
/// </summary>
public sealed record BatchRecord(
    Guid Id,
    string IdempotencyKey,
    string RequestFingerprint,
    string? FallbackDestinationCountry,
    BatchStatus Status,
    int TotalRows,
    int CompletedRows,
    int FailedRows,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    IReadOnlyList<BatchRowRecord> Rows);

/// <summary>
/// Reports whether an idempotent batch request created new durable work or
/// replayed the original batch.
/// </summary>
public sealed record BatchWriteResult(BatchRecord Batch, bool WasCreated);

/// <summary>
/// Carries one row claimed under a unique time-bounded lease. Completion must
/// present the same token to prevent stale workers from overwriting newer work.
/// </summary>
public sealed record BatchRowClaim(
    Guid ClaimToken,
    BatchRowRecord Row,
    DateTimeOffset LeaseExpiresAtUtc);
