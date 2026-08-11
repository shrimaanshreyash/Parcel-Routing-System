using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain;
using ParcelRoutingSystem.Domain.Parcels;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Claims and processes one durable row at a time so work can resume safely
/// after a process restart without duplicating completed decisions.
/// </summary>
public sealed class ProcessNextBatchRowUseCase
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    private readonly IBatchRepository _batches;
    private readonly IRuleSetRepository _ruleSets;
    private readonly IApplicationClock _clock;
    private readonly IIdentifierGenerator _identifiers;

    /// <summary>
    /// Creates the row processor with application ports for leases, rule sets,
    /// time, and identifiers.
    /// </summary>
    /// <param name="batches">The transactional durable batch repository.</param>
    /// <param name="ruleSets">The active immutable rule-set source.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    /// <param name="identifiers">The server-owned identifier generator.</param>
    public ProcessNextBatchRowUseCase(
        IBatchRepository batches,
        IRuleSetRepository ruleSets,
        IApplicationClock clock,
        IIdentifierGenerator identifiers)
    {
        _batches = batches;
        _ruleSets = ruleSets;
        _clock = clock;
        _identifiers = identifiers;
    }

    /// <summary>
    /// Claims one available row, evaluates it with the active rule set, and
    /// commits its decision and audit event atomically.
    /// </summary>
    /// <param name="workerId">The non-personal processor identity for auditing.</param>
    /// <param name="correlationId">The processor-loop correlation identifier.</param>
    /// <param name="cancellationToken">Cancels claim and persistence operations.</param>
    /// <returns>The processing outcome, or no-work when the queue is empty.</returns>
    public async Task<BatchRowProcessResult> ExecuteAsync(
        string workerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        OperationMetadata metadata = OperationMetadata.Create(workerId, correlationId);
        DateTimeOffset claimedAtUtc = _clock.UtcNow.ToUniversalTime();
        BatchRowClaim? claim = await _batches.ClaimNextAsync(
            claimedAtUtc,
            DefaultLeaseDuration,
            cancellationToken);

        if (claim is null)
        {
            return BatchRowProcessResult.NoWork();
        }

        try
        {
            StoredRuleSet active = await _ruleSets.GetActiveAsync(cancellationToken)
                ?? throw new ApplicationOperationException(
                    ApplicationErrorCodes.ActiveRuleSetUnavailable,
                    "No active routing rule set is available.");
            Parcel parcel = Parcel.Create(
                Weight.FromKilograms(claim.Row.WeightKilograms),
                DeclaredValue.FromEuros(claim.Row.DeclaredValueEuros),
                CountryCode.FromAlpha2(claim.Row.DestinationCountry));
            DateTimeOffset decidedAtUtc = _clock.UtcNow.ToUniversalTime();
            string decisionKey = $"batch:{claim.Row.BatchId:D}:row:{claim.Row.Id:D}";
            RoutingDecision decision = active.Definition.ToDomain().Route(
                parcel,
                RoutingDecisionContext.Create(
                    decidedAtUtc,
                    $"{metadata.CorrelationId}:{claim.Row.Id:D}"));
            var record = new RoutingDecisionRecord(
                _identifiers.NewId(),
                decisionKey,
                ApplicationRequestFingerprint.ForParcel(parcel),
                parcel.Weight.Kilograms,
                parcel.DeclaredValue.Euros,
                parcel.DestinationCountry.Value,
                decision.IntendedDepartment,
                decision.ApprovalState,
                decision.RuleSetVersion.Value,
                decision.MatchedRuleIds.Select(id => id.Value).ToArray(),
                decision.Reasons.ToArray(),
                decision.DecidedAtUtc,
                decision.CorrelationId,
                claim.Row.BatchId,
                claim.Row.Id);
            AuditEventRecord auditEvent = AuditEventRecord.Create(
                _identifiers.NewId(),
                "batch.row-completed",
                "batch-row",
                claim.Row.Id.ToString("D"),
                metadata,
                decisionKey,
                decidedAtUtc,
                new Dictionary<string, string>
                {
                    ["batchId"] = claim.Row.BatchId.ToString("D"),
                    ["decisionId"] = record.Id.ToString("D"),
                    ["department"] = record.IntendedDepartment.ToString(),
                    ["approvalState"] = record.ApprovalState.ToString(),
                });
            bool completed = await _batches.CompleteClaimAsync(
                claim,
                record,
                auditEvent,
                cancellationToken);

            return completed
                ? BatchRowProcessResult.Completed(claim.Row.Id, record.Id)
                : BatchRowProcessResult.LeaseLost(claim.Row.Id);
        }
        catch (ApplicationOperationException exception) when (
            exception.Code == ApplicationErrorCodes.ActiveRuleSetUnavailable)
        {
            return await DeferForRetryAsync(
                claim,
                metadata,
                exception,
                cancellationToken);
        }
        catch (DomainValidationException exception)
        {
            return await FailPermanentlyAsync(
                claim,
                metadata,
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// Releases a row for retry when a recoverable policy dependency is
    /// unavailable, preserving attempts and an audit trail without failing it.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="metadata">The worker and correlation metadata.</param>
    /// <param name="exception">The safe retryable application failure.</param>
    /// <param name="cancellationToken">Cancels the release transaction.</param>
    /// <returns>The deferred or lease-lost result.</returns>
    private async Task<BatchRowProcessResult> DeferForRetryAsync(
        BatchRowClaim claim,
        OperationMetadata metadata,
        ApplicationOperationException exception,
        CancellationToken cancellationToken)
    {
        string deferralKey =
            $"batch:{claim.Row.BatchId:D}:row:{claim.Row.Id:D}:deferred:{claim.Row.AttemptCount}";
        DateTimeOffset deferredAtUtc = _clock.UtcNow.ToUniversalTime();
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "batch.row-deferred",
            "batch-row",
            claim.Row.Id.ToString("D"),
            metadata,
            deferralKey,
            deferredAtUtc,
            new Dictionary<string, string>
            {
                ["errorCode"] = exception.Code,
            });
        bool released = await _batches.ReleaseClaimAsync(
            claim,
            exception.Code,
            exception.Message,
            auditEvent,
            cancellationToken);

        return released
            ? BatchRowProcessResult.Deferred(claim.Row.Id, exception.Code)
            : BatchRowProcessResult.LeaseLost(claim.Row.Id);
    }

    /// <summary>
    /// Converts a permanent domain or policy failure into an isolated durable row
    /// failure without terminating later rows in the batch.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="metadata">The worker and correlation metadata.</param>
    /// <param name="exception">The expected safe failure.</param>
    /// <param name="cancellationToken">Cancels the failure transaction.</param>
    /// <returns>The durable failure or lease-lost result.</returns>
    private async Task<BatchRowProcessResult> FailPermanentlyAsync(
        BatchRowClaim claim,
        OperationMetadata metadata,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string code = exception switch
        {
            DomainValidationException domain => domain.Code,
            ApplicationOperationException application => application.Code,
            _ => "routing.batch.row_failed",
        };
        string operatorMessage = exception is DomainValidationException domainFailure
            ? domainFailure.OperatorMessage
            : exception.Message;
        string failureKey = $"batch:{claim.Row.BatchId:D}:row:{claim.Row.Id:D}:failed";
        DateTimeOffset failedAtUtc = _clock.UtcNow.ToUniversalTime();
        AuditEventRecord auditEvent = AuditEventRecord.Create(
            _identifiers.NewId(),
            "batch.row-failed",
            "batch-row",
            claim.Row.Id.ToString("D"),
            metadata,
            failureKey,
            failedAtUtc,
            new Dictionary<string, string>
            {
                ["errorCode"] = code,
            });
        bool failed = await _batches.FailClaimAsync(
            claim,
            code,
            operatorMessage,
            auditEvent,
            cancellationToken);

        return failed
            ? BatchRowProcessResult.Failed(claim.Row.Id, code)
            : BatchRowProcessResult.LeaseLost(claim.Row.Id);
    }
}

/// <summary>
/// Identifies the observable outcome of one row-processing attempt.
/// </summary>
public enum BatchRowProcessStatus
{
    /// <summary>No pending or expired work was available.</summary>
    NoWork = 1,

    /// <summary>The row and immutable decision committed successfully.</summary>
    Completed = 2,

    /// <summary>The row committed a permanent isolated failure.</summary>
    Failed = 3,

    /// <summary>Another worker recovered the row before this lease committed.</summary>
    LeaseLost = 4,

    /// <summary>A retryable dependency failure returned the row to pending.</summary>
    Deferred = 5,
}

/// <summary>
/// Reports the row and optional decision or error produced by one processor
/// attempt.
/// </summary>
public sealed record BatchRowProcessResult(
    BatchRowProcessStatus Status,
    Guid? RowId,
    Guid? DecisionId,
    string? ErrorCode)
{
    /// <summary>
    /// Creates the empty-queue outcome without fabricating a row identity.
    /// </summary>
    /// <returns>A no-work processing result.</returns>
    public static BatchRowProcessResult NoWork()
    {
        return new BatchRowProcessResult(
            BatchRowProcessStatus.NoWork,
            RowId: null,
            DecisionId: null,
            ErrorCode: null);
    }

    /// <summary>
    /// Creates a successful row and decision outcome.
    /// </summary>
    /// <param name="rowId">The completed durable row.</param>
    /// <param name="decisionId">The new immutable decision.</param>
    /// <returns>A completed processing result.</returns>
    public static BatchRowProcessResult Completed(Guid rowId, Guid decisionId)
    {
        return new BatchRowProcessResult(
            BatchRowProcessStatus.Completed,
            rowId,
            decisionId,
            ErrorCode: null);
    }

    /// <summary>
    /// Creates an isolated permanent row-failure outcome.
    /// </summary>
    /// <param name="rowId">The failed durable row.</param>
    /// <param name="errorCode">The stable safe failure category.</param>
    /// <returns>A failed processing result.</returns>
    public static BatchRowProcessResult Failed(Guid rowId, string errorCode)
    {
        return new BatchRowProcessResult(
            BatchRowProcessStatus.Failed,
            rowId,
            DecisionId: null,
            errorCode);
    }

    /// <summary>
    /// Creates the stale-worker outcome used when the current claim token no
    /// longer owns the row.
    /// </summary>
    /// <param name="rowId">The row whose lease was lost.</param>
    /// <returns>A lease-lost processing result.</returns>
    public static BatchRowProcessResult LeaseLost(Guid rowId)
    {
        return new BatchRowProcessResult(
            BatchRowProcessStatus.LeaseLost,
            rowId,
            DecisionId: null,
            ErrorCode: null);
    }

    /// <summary>
    /// Creates a retryable deferral outcome after the row returns to pending.
    /// </summary>
    /// <param name="rowId">The released durable row.</param>
    /// <param name="errorCode">The stable retryable failure category.</param>
    /// <returns>A deferred processing result.</returns>
    public static BatchRowProcessResult Deferred(Guid rowId, string errorCode)
    {
        return new BatchRowProcessResult(
            BatchRowProcessStatus.Deferred,
            rowId,
            DecisionId: null,
            errorCode);
    }
}
