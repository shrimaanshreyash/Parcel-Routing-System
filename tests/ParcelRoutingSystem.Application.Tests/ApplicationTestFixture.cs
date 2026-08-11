using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Application.Tests;

/// <summary>
/// Provides deterministic application ports and a transaction-like in-memory
/// repository for testing orchestration without a database framework.
/// </summary>
internal static class ApplicationTestFixture
{
    internal static readonly DateTimeOffset FixedTime =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates the validated operation metadata shared by application tests.
    /// </summary>
    /// <param name="correlationId">The deterministic correlation identifier.</param>
    /// <returns>Non-personal actor and correlation metadata.</returns>
    internal static OperationMetadata Metadata(string correlationId = "test-correlation")
    {
        return OperationMetadata.Create("actor-001", correlationId);
    }

    /// <summary>
    /// Creates a repository preloaded with the default active rule-set version.
    /// </summary>
    /// <returns>A fresh isolated application store.</returns>
    internal static InMemoryApplicationStore CreateStore()
    {
        return new InMemoryApplicationStore(
            new StoredRuleSet(
                RuleSetDefinition.FromDomain(RoutingRuleSet.CreateDefault()),
                RuleSetLifecycleStatus.Active,
                FixedTime,
                "system",
                FixedTime));
    }
}

/// <summary>
/// Supplies a controllable UTC timestamp so application tests can advance lease
/// recovery without sleeping.
/// </summary>
internal sealed class MutableClock : IApplicationClock
{
    /// <summary>
    /// Creates a clock at the supplied deterministic UTC instant.
    /// </summary>
    /// <param name="utcNow">The initial test time.</param>
    internal MutableClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    /// <summary>Gets or sets the current deterministic test time.</summary>
    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>
/// Generates deterministic unique identifiers for assertions without relying
/// on random GUID generation.
/// </summary>
internal sealed class SequenceIdentifierGenerator : IIdentifierGenerator
{
    private int _next;

    /// <summary>
    /// Creates the next deterministic non-empty GUID in the sequence.
    /// </summary>
    /// <returns>A stable unique test identifier.</returns>
    public Guid NewId()
    {
        _next++;
        return new Guid($"00000000-0000-0000-0000-{_next:D12}");
    }
}

/// <summary>
/// Implements all application ports for orchestration tests. This is
/// deliberately a test double; PostgreSQL transaction behavior is verified
/// separately in infrastructure integration tests.
/// </summary>
internal sealed class InMemoryApplicationStore :
    IRuleSetRepository,
    IRoutingDecisionRepository,
    IInsuranceApprovalRepository,
    IBatchRepository
{
    private readonly Dictionary<int, StoredRuleSet> _ruleSets = [];
    private readonly Dictionary<string, RuleSetWriteResult> _ruleWrites = [];
    private readonly Dictionary<string, RuleSetActivationResult> _activations = [];
    private readonly Dictionary<string, RoutingDecisionRecord> _decisions = [];
    private readonly Dictionary<string, InsuranceApprovalRecord> _approvals = [];
    private readonly Dictionary<Guid, BatchRecord> _batches = [];
    private readonly Dictionary<string, Guid> _batchKeys = [];
    private readonly Dictionary<Guid, (Guid Token, DateTimeOffset ExpiresAt)> _claims = [];

    /// <summary>
    /// Creates an optional active rule-set baseline for routing tests.
    /// </summary>
    /// <param name="activeRuleSet">The active version, or null for fail-closed tests.</param>
    internal InMemoryApplicationStore(StoredRuleSet? activeRuleSet = null)
    {
        if (activeRuleSet is not null)
        {
            _ruleSets.Add(activeRuleSet.Definition.Version, activeRuleSet);
        }
    }

    /// <summary>Gets captured append-only audit events for assertions.</summary>
    internal List<AuditEventRecord> AuditEvents { get; } = [];

    /// <summary>Gets durable decisions keyed by idempotency key.</summary>
    internal IReadOnlyDictionary<string, RoutingDecisionRecord> Decisions => _decisions;

    /// <summary>
    /// Returns the single in-memory active rule-set version.
    /// </summary>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The active version, or null when none is configured.</returns>
    public Task<StoredRuleSet?> GetActiveAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _ruleSets.Values.SingleOrDefault(
                ruleSet => ruleSet.Status == RuleSetLifecycleStatus.Active));
    }

    /// <summary>
    /// Returns one stored in-memory rule-set version.
    /// </summary>
    /// <param name="version">The immutable version number.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The version, or null when absent.</returns>
    public Task<StoredRuleSet?> GetVersionAsync(
        int version,
        CancellationToken cancellationToken)
    {
        _ruleSets.TryGetValue(version, out StoredRuleSet? ruleSet);
        return Task.FromResult(ruleSet);
    }

    /// <summary>
    /// Returns newest stored rule versions under the requested test bound.
    /// </summary>
    public Task<IReadOnlyList<StoredRuleSet>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredRuleSet> versions = _ruleSets.Values
            .OrderByDescending(ruleSet => ruleSet.Definition.Version)
            .Take(limit)
            .ToArray();
        return Task.FromResult(versions);
    }

    /// <summary>
    /// Stores one draft and audit event once per idempotency key.
    /// </summary>
    /// <param name="draft">The proposed validated draft.</param>
    /// <param name="auditEvent">The draft audit event.</param>
    /// <param name="idempotencyKey">The replay key.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The first or replayed draft result.</returns>
    public Task<RuleSetWriteResult> SaveDraftAsync(
        StoredRuleSet draft,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_ruleWrites.TryGetValue(idempotencyKey, out RuleSetWriteResult? replay))
        {
            return Task.FromResult(replay with { WasCreated = false });
        }

        _ruleSets.Add(draft.Definition.Version, draft);
        var result = new RuleSetWriteResult(draft, WasCreated: true);
        _ruleWrites.Add(idempotencyKey, result);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Atomically changes the in-memory active version once per idempotency key.
    /// </summary>
    /// <param name="version">The version to activate.</param>
    /// <param name="auditEvent">The activation audit event.</param>
    /// <param name="idempotencyKey">The replay key.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The first or replayed activation result.</returns>
    public Task<RuleSetActivationResult> ActivateAsync(
        int version,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_activations.TryGetValue(
                idempotencyKey,
                out RuleSetActivationResult? replay))
        {
            return Task.FromResult(replay with { WasChanged = false });
        }

        if (!_ruleSets.TryGetValue(version, out StoredRuleSet? selected))
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.RuleSetNotFound,
                "The requested rule set does not exist.");
        }

        foreach ((int key, StoredRuleSet value) in _ruleSets.ToArray())
        {
            if (value.Status == RuleSetLifecycleStatus.Active)
            {
                _ruleSets[key] = value with
                {
                    Status = RuleSetLifecycleStatus.Retired,
                };
            }
        }

        StoredRuleSet active = selected with
        {
            Status = RuleSetLifecycleStatus.Active,
            ActivatedAtUtc = auditEvent.OccurredAtUtc,
        };
        _ruleSets[version] = active;
        var result = new RuleSetActivationResult(active, WasChanged: true);
        _activations.Add(idempotencyKey, result);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Finds the original in-memory decision for a replay key.
    /// </summary>
    /// <param name="idempotencyKey">The routing replay key.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The original decision, or null.</returns>
    public Task<RoutingDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _decisions.TryGetValue(idempotencyKey, out RoutingDecisionRecord? decision);
        return Task.FromResult(decision);
    }

    /// <summary>
    /// Stores one in-memory decision and audit event or replays the first write.
    /// </summary>
    /// <param name="decision">The proposed immutable decision.</param>
    /// <param name="auditEvent">The decision audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The first or replayed decision write.</returns>
    public Task<DecisionWriteResult> SaveAsync(
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (_decisions.TryGetValue(
                decision.IdempotencyKey,
                out RoutingDecisionRecord? replay))
        {
            return Task.FromResult(new DecisionWriteResult(replay, WasCreated: false));
        }

        _decisions.Add(decision.IdempotencyKey, decision);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(new DecisionWriteResult(decision, WasCreated: true));
    }

    /// <summary>
    /// Applies the approval decision-state checks and idempotent write in memory.
    /// </summary>
    /// <param name="approval">The proposed approval.</param>
    /// <param name="auditEvent">The approval audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The explicit approval outcome.</returns>
    public Task<InsuranceApprovalWriteResult> ApproveAsync(
        InsuranceApprovalRecord approval,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (_approvals.TryGetValue(
                approval.IdempotencyKey,
                out InsuranceApprovalRecord? replay))
        {
            return Task.FromResult(
                new InsuranceApprovalWriteResult(
                    InsuranceApprovalWriteStatus.Replayed,
                    replay));
        }

        RoutingDecisionRecord? decision = _decisions.Values.SingleOrDefault(
            item => item.Id == approval.DecisionId);
        if (decision is null)
        {
            return Task.FromResult(
                new InsuranceApprovalWriteResult(
                    InsuranceApprovalWriteStatus.DecisionNotFound,
                    Approval: null));
        }

        if (decision.ApprovalState != ApprovalState.PendingInsuranceApproval)
        {
            return Task.FromResult(
                new InsuranceApprovalWriteResult(
                    InsuranceApprovalWriteStatus.ApprovalNotRequired,
                    Approval: null));
        }

        InsuranceApprovalRecord? existingForDecision = _approvals.Values
            .SingleOrDefault(item => item.DecisionId == approval.DecisionId);
        if (existingForDecision is not null)
        {
            return Task.FromResult(
                new InsuranceApprovalWriteResult(
                    InsuranceApprovalWriteStatus.Replayed,
                    existingForDecision));
        }

        _approvals.Add(approval.IdempotencyKey, approval);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(
            new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.Created,
                approval));
    }

    /// <summary>
    /// Stores one in-memory batch graph and audit event idempotently.
    /// </summary>
    /// <param name="batch">The proposed batch.</param>
    /// <param name="auditEvent">The creation audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The first or replayed batch result.</returns>
    public Task<BatchWriteResult> SaveBatchAsync(
        BatchRecord batch,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (_batchKeys.TryGetValue(batch.IdempotencyKey, out Guid existingId))
        {
            return Task.FromResult(
                new BatchWriteResult(_batches[existingId], WasCreated: false));
        }

        _batches.Add(batch.Id, batch);
        _batchKeys.Add(batch.IdempotencyKey, batch.Id);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(new BatchWriteResult(batch, WasCreated: true));
    }

    /// <summary>
    /// Returns the original in-memory batch for a repeated operation key.
    /// </summary>
    public Task<BatchRecord?> FindBatchByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        BatchRecord? batch = _batchKeys.TryGetValue(
            idempotencyKey,
            out Guid batchId)
            ? _batches[batchId]
            : null;
        return Task.FromResult(batch);
    }

    /// <summary>
    /// Returns the newest in-memory batch with the same normalized fingerprint.
    /// </summary>
    public Task<BatchRecord?> FindLatestByFingerprintAsync(
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        BatchRecord? batch = _batches.Values
            .Where(
                item => string.Equals(
                    item.RequestFingerprint,
                    requestFingerprint,
                    StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();
        return Task.FromResult(batch);
    }

    /// <summary>
    /// Claims the first pending or expired in-memory row under a new token.
    /// </summary>
    /// <param name="claimedAtUtc">The deterministic claim time.</param>
    /// <param name="leaseDuration">The test lease duration.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The claimed row, or null when none is available.</returns>
    public Task<BatchRowClaim?> ClaimNextAsync(
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        foreach (BatchRecord batch in _batches.Values.OrderBy(item => item.CreatedAtUtc))
        {
            BatchRowRecord? row = batch.Rows
                .OrderBy(item => item.RowNumber)
                .FirstOrDefault(
                    item => item.Status == BatchRowStatus.Pending
                        || (item.Status == BatchRowStatus.Processing
                            && _claims.TryGetValue(item.Id, out var lease)
                            && lease.ExpiresAt <= claimedAtUtc));

            if (row is null)
            {
                continue;
            }

            Guid token = Guid.NewGuid();
            DateTimeOffset expiresAt = claimedAtUtc.Add(leaseDuration);
            BatchRowRecord claimed = row with
            {
                Status = BatchRowStatus.Processing,
                AttemptCount = row.AttemptCount + 1,
            };
            ReplaceRow(batch, claimed);
            _claims[row.Id] = (token, expiresAt);
            return Task.FromResult<BatchRowClaim?>(
                new BatchRowClaim(token, claimed, expiresAt));
        }

        return Task.FromResult<BatchRowClaim?>(null);
    }

    /// <summary>
    /// Completes the current in-memory row lease with an immutable decision.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="decision">The row decision.</param>
    /// <param name="auditEvent">The row completion audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>True when the lease was current.</returns>
    public Task<bool> CompleteClaimAsync(
        BatchRowClaim claim,
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (!OwnsClaim(claim))
        {
            return Task.FromResult(false);
        }

        BatchRecord batch = _batches[claim.Row.BatchId];
        BatchRowRecord completed = claim.Row with
        {
            Status = BatchRowStatus.Completed,
            DecisionId = decision.Id,
        };
        _decisions.Add(decision.IdempotencyKey, decision);
        ReplaceRow(batch, completed);
        _claims.Remove(claim.Row.Id);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Fails the current in-memory row lease while preserving other rows.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The stable failure code.</param>
    /// <param name="errorMessage">The safe failure explanation.</param>
    /// <param name="auditEvent">The row failure audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>True when the lease was current.</returns>
    public Task<bool> FailClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (!OwnsClaim(claim))
        {
            return Task.FromResult(false);
        }

        BatchRecord batch = _batches[claim.Row.BatchId];
        BatchRowRecord failed = claim.Row with
        {
            Status = BatchRowStatus.ProcessingFailed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
        ReplaceRow(batch, failed);
        _claims.Remove(claim.Row.Id);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns a retryable in-memory row to pending state without incrementing
    /// permanent failure counters.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The retryable failure code.</param>
    /// <param name="errorMessage">The safe failure explanation.</param>
    /// <param name="auditEvent">The deferral audit event.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>True when the lease was current.</returns>
    public Task<bool> ReleaseClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        if (!OwnsClaim(claim))
        {
            return Task.FromResult(false);
        }

        BatchRecord batch = _batches[claim.Row.BatchId];
        BatchRowRecord deferred = claim.Row with
        {
            Status = BatchRowStatus.Pending,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
        ReplaceRow(batch, deferred);
        _claims.Remove(claim.Row.Id);
        AuditEvents.Add(auditEvent);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns the current in-memory batch snapshot.
    /// </summary>
    /// <param name="batchId">The batch identifier.</param>
    /// <param name="cancellationToken">Unused test cancellation signal.</param>
    /// <returns>The current batch, or null.</returns>
    public Task<BatchRecord?> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        _batches.TryGetValue(batchId, out BatchRecord? batch);
        return Task.FromResult(batch);
    }

    /// <summary>
    /// Confirms that the supplied token still owns the row's current lease.
    /// </summary>
    /// <param name="claim">The claim to verify.</param>
    /// <returns>True only for the latest token.</returns>
    private bool OwnsClaim(BatchRowClaim claim)
    {
        return _claims.TryGetValue(claim.Row.Id, out var current)
            && current.Token == claim.ClaimToken;
    }

    /// <summary>
    /// Replaces one row and recomputes batch counters and terminal status.
    /// </summary>
    /// <param name="batch">The current batch snapshot.</param>
    /// <param name="replacement">The replacement row state.</param>
    private void ReplaceRow(BatchRecord batch, BatchRowRecord replacement)
    {
        BatchRowRecord[] rows = batch.Rows
            .Select(row => row.Id == replacement.Id ? replacement : row)
            .ToArray();
        int completed = rows.Count(row => row.Status == BatchRowStatus.Completed);
        int failed = rows.Count(
            row => row.Status is BatchRowStatus.ValidationFailed
                or BatchRowStatus.ProcessingFailed);
        int finished = completed + failed;
        BatchStatus status = finished == rows.Length
            ? failed == 0
                ? BatchStatus.Completed
                : BatchStatus.CompletedWithErrors
            : rows.Any(row => row.Status == BatchRowStatus.Processing)
                ? BatchStatus.Processing
                : BatchStatus.Pending;
        _batches[batch.Id] = batch with
        {
            Status = status,
            CompletedRows = completed,
            FailedRows = failed,
            Rows = rows,
        };
    }
}
