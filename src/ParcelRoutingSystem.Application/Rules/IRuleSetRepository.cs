using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Rules;

/// <summary>
/// Defines persistence operations for immutable rule versions. Implementations
/// must keep activation and its audit event in one transaction.
/// </summary>
public interface IRuleSetRepository
{
    /// <summary>
    /// Loads the single active rule set or returns null when routing must fail
    /// closed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the persistence operation.</param>
    /// <returns>The active immutable version, or null when none exists.</returns>
    Task<StoredRuleSet?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads one historical or draft version for simulation and activation.
    /// </summary>
    /// <param name="version">The immutable version number.</param>
    /// <param name="cancellationToken">Cancels the persistence operation.</param>
    /// <returns>The requested version, or null when it does not exist.</returns>
    Task<StoredRuleSet?> GetVersionAsync(int version, CancellationToken cancellationToken);

    /// <summary>
    /// Loads newest immutable rule-set versions under a strict bounded limit so
    /// administration can monitor and select a rollback target safely.
    /// </summary>
    Task<IReadOnlyList<StoredRuleSet>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a validated draft and audit event atomically, returning the
    /// original result when the same idempotency key is replayed.
    /// </summary>
    /// <param name="draft">The validated immutable draft.</param>
    /// <param name="auditEvent">The corresponding append-only audit event.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The stored draft and whether it was newly created.</returns>
    Task<RuleSetWriteResult> SaveDraftAsync(
        StoredRuleSet draft,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically activates the requested validated version, retires the
    /// previous active version, and appends one audit event.
    /// </summary>
    /// <param name="version">The draft or historical version to activate.</param>
    /// <param name="auditEvent">The activation or rollback audit event.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The resulting active version and whether state changed.</returns>
    Task<RuleSetActivationResult> ActivateAsync(
        int version,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
