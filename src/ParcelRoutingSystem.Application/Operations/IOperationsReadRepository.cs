namespace ParcelRoutingSystem.Application.Operations;

/// <summary>
/// Defines privacy-safe operational queries separately from write repositories
/// so HTTP readers cannot mutate persistence entities accidentally.
/// </summary>
public interface IOperationsReadRepository
{
    /// <summary>
    /// Loads current UTC-day counters and one bounded decision-history page.
    /// </summary>
    /// <param name="utcDayStart">The inclusive start of the current UTC day.</param>
    /// <param name="range">The selected allow-listed history preset.</param>
    /// <param name="filter">The selected allow-listed decision category.</param>
    /// <param name="rangeStartUtc">Optional inclusive history-window boundary.</param>
    /// <param name="recentOnly">Whether to return only the newest bounded page.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The bounded number of decisions per page.</param>
    /// <param name="cancellationToken">Cancels database queries.</param>
    /// <returns>The current privacy-safe operations overview.</returns>
    Task<OperationsOverview> GetOverviewAsync(
        DateTimeOffset utcDayStart,
        OperationsTimeRange range,
        RoutingDecisionFilter filter,
        DateTimeOffset? rangeStartUtc,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one newest-first page of privacy-safe audit records.
    /// </summary>
    /// <param name="rangeStartUtc">Optional inclusive history-window boundary.</param>
    /// <param name="category">The selected allow-listed activity category.</param>
    /// <param name="recentOnly">Whether to return only the newest bounded page.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The bounded number of events per page.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>One event page ordered from newest to oldest.</returns>
    Task<PagedResults<ActivityRecord>> GetActivityAsync(
        DateTimeOffset? rangeStartUtc,
        ActivityCategory category,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one bounded page of either current import failures or durable rows
    /// still waiting for completion.
    /// </summary>
    /// <param name="kind">The actionable import state requested by the operator.</param>
    /// <param name="utcDayStart">The inclusive UTC-day boundary for issue counts.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The bounded number of rows returned.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>One privacy-safe attention page ordered predictably.</returns>
    Task<PagedResults<ImportAttentionItem>> GetImportAttentionAsync(
        ImportAttentionKind kind,
        DateTimeOffset utcDayStart,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one batch plus immutable decisions associated with completed rows.
    /// </summary>
    /// <param name="batchId">The server-owned batch identifier.</param>
    /// <param name="cancellationToken">Cancels database queries.</param>
    /// <returns>The joined batch view or null when the batch does not exist.</returns>
    Task<BatchDetails?> GetBatchDetailsAsync(
        Guid batchId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads recent durable imports without their row graphs under a strict
    /// bounded limit.
    /// </summary>
    Task<IReadOnlyList<BatchSummary>> GetRecentBatchesAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one privacy-safe decision with optional append-only approval
    /// evidence.
    /// </summary>
    Task<RoutingDecisionDetails?> GetDecisionAsync(
        Guid decisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads one bounded page of oldest unresolved insurance holds so
    /// approvers have a deterministic work queue.
    /// </summary>
    Task<PagedResults<RoutingDecisionSummary>> GetAwaitingInsuranceAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
