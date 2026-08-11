using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Routing;

/// <summary>
/// Defines immutable decision persistence. Implementations must enforce unique
/// idempotency keys and persist a new decision with its audit event atomically.
/// </summary>
public interface IRoutingDecisionRepository
{
    /// <summary>
    /// Finds the original immutable outcome of a previously accepted request.
    /// </summary>
    /// <param name="idempotencyKey">The normalized operation replay key.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>The original decision, or null when this request is new.</returns>
    Task<RoutingDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new immutable decision and audit event in one transaction, or
    /// returns the winner of a concurrent request with the same key.
    /// </summary>
    /// <param name="decision">The proposed immutable decision.</param>
    /// <param name="auditEvent">The corresponding privacy-safe audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable decision and whether this call created it.</returns>
    Task<DecisionWriteResult> SaveAsync(
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken);
}
