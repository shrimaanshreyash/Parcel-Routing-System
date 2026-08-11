using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;

namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Defines transactional durable-batch operations. Implementations must claim
/// rows atomically and commit row state, decision, counters, and audit together.
/// </summary>
public interface IBatchRepository
{
    /// <summary>
    /// Loads a batch already bound to one operation key so a network retry is
    /// replayed before duplicate-manifest policy is evaluated.
    /// </summary>
    /// <param name="idempotencyKey">The normalized operation replay key.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>The original batch, or null for a new operation.</returns>
    Task<BatchRecord?> FindBatchByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the newest prior import with the same privacy-safe normalized
    /// manifest fingerprint and fallback context.
    /// </summary>
    /// <param name="requestFingerprint">The normalized SHA-256 fingerprint.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>The newest matching batch, or null when none exists.</returns>
    Task<BatchRecord?> FindLatestByFingerprintAsync(
        string requestFingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores one batch, all rows, and its audit event atomically or returns the
    /// original batch for an idempotent replay.
    /// </summary>
    /// <param name="batch">The proposed privacy-minimized batch.</param>
    /// <param name="auditEvent">The corresponding creation audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable batch and whether it was newly created.</returns>
    Task<BatchWriteResult> SaveBatchAsync(
        BatchRecord batch,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims the oldest pending or expired row under a new lease so
    /// multiple processors cannot work the same current attempt.
    /// </summary>
    /// <param name="claimedAtUtc">The server-owned claim timestamp.</param>
    /// <param name="leaseDuration">The bounded time before restart recovery.</param>
    /// <param name="cancellationToken">Cancels the claim transaction.</param>
    /// <returns>A claimed row, or null when no work is available.</returns>
    Task<BatchRowClaim?> ClaimNextAsync(
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically stores a row decision, marks the row complete, updates batch
    /// counters, and appends its audit event when the lease token is current.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="decision">The immutable routing decision.</param>
    /// <param name="auditEvent">The corresponding row completion event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease completed the row.</returns>
    Task<bool> CompleteClaimAsync(
        BatchRowClaim claim,
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically records a permanent row failure, updates batch counters, and
    /// appends a privacy-safe audit event when the lease token is current.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The stable failure category.</param>
    /// <param name="errorMessage">The safe non-personal failure explanation.</param>
    /// <param name="auditEvent">The corresponding row failure event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease failed the row.</returns>
    Task<bool> FailClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases a retryable row back to pending state and appends a safe audit
    /// event without incrementing permanent failure counters.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The stable retryable failure category.</param>
    /// <param name="errorMessage">The safe non-personal explanation.</param>
    /// <param name="auditEvent">The corresponding deferral audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease released the row.</returns>
    Task<bool> ReleaseClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads a batch with its current row states for progress inspection.
    /// </summary>
    /// <param name="batchId">The server-owned batch identifier.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>The current batch snapshot, or null when absent.</returns>
    Task<BatchRecord?> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken);
}
