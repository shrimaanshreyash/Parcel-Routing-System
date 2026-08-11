using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Operations;

/// <summary>
/// Coordinates bounded, privacy-safe operational reads for the API while time
/// and persistence remain replaceable application-owned ports.
/// </summary>
public sealed class OperationsQueryUseCase
{
    private const int RecentDecisionLimit = 10;
    private const int PagedResultSize = 15;
    private const int MaximumBatchHistoryLimit = 50;

    private readonly IOperationsReadRepository _repository;
    private readonly IApplicationClock _clock;

    /// <summary>
    /// Creates the query coordinator around durable read storage and a
    /// server-owned UTC clock.
    /// </summary>
    /// <param name="repository">The privacy-safe operations read repository.</param>
    /// <param name="clock">The server-owned UTC clock.</param>
    public OperationsQueryUseCase(
        IOperationsReadRepository repository,
        IApplicationClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    /// <summary>
    /// Loads current operational counters and one bounded decision-history
    /// window for the operator overview.
    /// </summary>
    /// <param name="range">The constrained server-owned history window.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="cancellationToken">Cancels persistence reads.</param>
    /// <returns>The current operational overview.</returns>
    public Task<OperationsOverview> GetOverviewAsync(
        OperationsTimeRange range = OperationsTimeRange.Recent,
        int page = 1,
        RoutingDecisionFilter filter = RoutingDecisionFilter.All,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow.ToUniversalTime();
        var utcDayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return _repository.GetOverviewAsync(
            utcDayStart,
            range,
            filter,
            ResolveRangeStart(range, now),
            range == OperationsTimeRange.Recent,
            range == OperationsTimeRange.Recent ? 1 : Math.Max(page, 1),
            range == OperationsTimeRange.Recent
                ? RecentDecisionLimit
                : PagedResultSize,
            cancellationToken);
    }

    /// <summary>
    /// Loads one bounded newest-first audit page for a constrained time window.
    /// </summary>
    /// <param name="range">The constrained server-owned history window.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>One page of newest events first.</returns>
    public Task<PagedResults<ActivityRecord>> GetActivityAsync(
        OperationsTimeRange range = OperationsTimeRange.Recent,
        int page = 1,
        ActivityCategory category = ActivityCategory.All,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow.ToUniversalTime();
        return _repository.GetActivityAsync(
            ResolveRangeStart(range, now),
            category,
            range == OperationsTimeRange.Recent,
            range == OperationsTimeRange.Recent ? 1 : Math.Max(page, 1),
            PagedResultSize,
            cancellationToken);
    }

    /// <summary>
    /// Loads the concrete import rows represented by the Overview issue or
    /// queue KPI so every status count has an actionable bounded destination.
    /// </summary>
    /// <param name="kind">Whether the operator is investigating failures or current work.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>One page of privacy-safe import attention rows.</returns>
    public Task<PagedResults<ImportAttentionItem>> GetImportAttentionAsync(
        ImportAttentionKind kind,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow.ToUniversalTime();
        var utcDayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return _repository.GetImportAttentionAsync(
            kind,
            utcDayStart,
            Math.Max(page, 1),
            PagedResultSize,
            cancellationToken);
    }

    /// <summary>
    /// Loads one durable batch and its completed routing outcomes, failing with
    /// a stable code when the public identifier does not exist.
    /// </summary>
    /// <param name="batchId">The server-owned non-sequential batch identifier.</param>
    /// <param name="cancellationToken">Cancels persistence reads.</param>
    /// <returns>The current batch and decision details.</returns>
    public async Task<BatchDetails> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.BatchNotFound,
                "The batch does not exist.");
        }

        return await _repository.GetBatchDetailsAsync(batchId, cancellationToken)
            ?? throw new ApplicationOperationException(
                ApplicationErrorCodes.BatchNotFound,
                "The batch does not exist.");
    }

    /// <summary>
    /// Loads a bounded recent-import history so navigation never depends on
    /// component-local browser state.
    /// </summary>
    public Task<IReadOnlyList<BatchSummary>> GetRecentBatchesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentBatchesAsync(
            Math.Clamp(limit, 1, MaximumBatchHistoryLimit),
            cancellationToken);
    }

    /// <summary>
    /// Loads one immutable decision and its separate approval evidence.
    /// </summary>
    public async Task<RoutingDecisionDetails> GetDecisionAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.DecisionNotFound,
                "The routing decision does not exist.");
        }

        return await _repository.GetDecisionAsync(decisionId, cancellationToken)
            ?? throw new ApplicationOperationException(
                ApplicationErrorCodes.DecisionNotFound,
                "The routing decision does not exist.");
    }

    /// <summary>
    /// Loads one bounded oldest-first insurance work-queue page.
    /// </summary>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="cancellationToken">Cancels the persistence read.</param>
    /// <returns>One stable page of unresolved insurance holds.</returns>
    public Task<PagedResults<RoutingDecisionSummary>> GetAwaitingInsuranceAsync(
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetAwaitingInsuranceAsync(
            Math.Max(page, 1),
            PagedResultSize,
            cancellationToken);
    }

    /// <summary>
    /// Converts a supported relative history preset into one inclusive UTC
    /// boundary while Recent and All time deliberately remain unbounded.
    /// </summary>
    /// <param name="range">The allow-listed history preset.</param>
    /// <param name="now">The server-owned current UTC time.</param>
    /// <returns>The inclusive UTC boundary, or null for bounded recent/all-time reads.</returns>
    private static DateTimeOffset? ResolveRangeStart(
        OperationsTimeRange range,
        DateTimeOffset now)
    {
        return range switch
        {
            OperationsTimeRange.Recent => null,
            OperationsTimeRange.Last24Hours => now.AddHours(-24),
            OperationsTimeRange.Last7Days => now.AddDays(-7),
            OperationsTimeRange.Last30Days => now.AddDays(-30),
            OperationsTimeRange.Last12Months => now.AddYears(-1),
            OperationsTimeRange.AllTime => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(range),
                range,
                "The operations time range is not supported."),
        };
    }
}
