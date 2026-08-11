using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Operations;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Builds bounded privacy-safe operator read models directly from PostgreSQL
/// without exposing tracked EF entities to application or HTTP layers.
/// </summary>
public sealed class PostgresOperationsReadRepository : IOperationsReadRepository
{
    private readonly ParcelRoutingDbContext _context;

    /// <summary>
    /// Creates the PostgreSQL operations reader around one scoped EF context.
    /// </summary>
    /// <param name="context">The scoped persistence context.</param>
    public PostgresOperationsReadRepository(ParcelRoutingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Loads current counters and one bounded history page sequentially on the
    /// scoped context so one DbContext never executes concurrent operations.
    /// </summary>
    /// <param name="utcDayStart">The inclusive UTC-day boundary.</param>
    /// <param name="range">The selected allow-listed history preset.</param>
    /// <param name="filter">The selected allow-listed decision category.</param>
    /// <param name="rangeStartUtc">The inclusive history boundary, if constrained.</param>
    /// <param name="recentOnly">Whether the caller requested the newest bounded set.</param>
    /// <param name="page">The requested one-based history page.</param>
    /// <param name="pageSize">The maximum number of decisions returned.</param>
    /// <param name="cancellationToken">Cancels database work.</param>
    /// <returns>The current operator overview.</returns>
    public async Task<OperationsOverview> GetOverviewAsync(
        DateTimeOffset utcDayStart,
        OperationsTimeRange range,
        RoutingDecisionFilter filter,
        DateTimeOffset? rangeStartUtc,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        int totalDecisions = await _context.RoutingDecisions
            .AsNoTracking()
            .CountAsync(cancellationToken);
        int processedToday = await _context.RoutingDecisions
            .AsNoTracking()
            .CountAsync(
                decision => decision.DecidedAtUtc >= utcDayStart,
                cancellationToken);
        int awaitingApproval = await _context.RoutingDecisions
            .AsNoTracking()
            .CountAsync(
                decision => decision.ApprovalState
                        == ApprovalState.PendingInsuranceApproval
                    && !_context.InsuranceApprovals.Any(
                        approval => approval.DecisionId == decision.Id),
                cancellationToken);
        int importIssues = await _context.BatchRows
            .AsNoTracking()
            .CountAsync(
                row => row.Batch.CreatedAtUtc >= utcDayStart
                    && (row.Status == BatchRowStatus.ValidationFailed
                        || row.Status == BatchRowStatus.ProcessingFailed),
                cancellationToken);
        int pendingBatchRows = await _context.BatchRows
            .AsNoTracking()
            .CountAsync(
                row => row.Status == BatchRowStatus.Pending
                    || row.Status == BatchRowStatus.Processing,
                cancellationToken);

        IQueryable<RoutingDecisionEntity> historyQuery =
            _context.RoutingDecisions.AsNoTracking();
        historyQuery = ApplyDecisionFilter(historyQuery, filter);
        if (rangeStartUtc is DateTimeOffset rangeStart)
        {
            historyQuery = historyQuery.Where(
                decision => decision.DecidedAtUtc >= rangeStart);
        }

        int matchingDecisions = await historyQuery.CountAsync(cancellationToken);
        int totalHistoryItems = recentOnly
            ? Math.Min(matchingDecisions, pageSize)
            : matchingDecisions;
        RoutingDecisionEntity[] history = await historyQuery
            .OrderByDescending(decision => decision.DecidedAtUtc)
            .ThenByDescending(decision => decision.Id)
            .Skip(recentOnly ? 0 : (page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        HashSet<Guid> approvedDecisionIds = await LoadApprovedDecisionIdsAsync(
            history.Select(decision => decision.Id),
            cancellationToken);
        RoutingDecisionSummary[] summaries = history
            .Select(decision => ToSummary(decision, approvedDecisionIds))
            .ToArray();

        return new OperationsOverview(
            totalDecisions,
            processedToday,
            awaitingApproval,
            importIssues,
            pendingBatchRows,
            range,
            filter,
            new PagedResults<RoutingDecisionSummary>(
                summaries,
                recentOnly ? 1 : page,
                pageSize,
                totalHistoryItems));
    }

    /// <summary>
    /// Loads one newest-first audit page and deserializes only the controlled
    /// string-to-string details written by application audit factories.
    /// </summary>
    /// <param name="rangeStartUtc">The inclusive history boundary, if constrained.</param>
    /// <param name="category">The selected allow-listed activity category.</param>
    /// <param name="recentOnly">Whether to return only the newest bounded page.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="pageSize">The maximum number of events returned.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>One bounded page of privacy-safe events.</returns>
    public async Task<PagedResults<ActivityRecord>> GetActivityAsync(
        DateTimeOffset? rangeStartUtc,
        ActivityCategory category,
        bool recentOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditEventEntity> activityQuery =
            _context.AuditEvents.AsNoTracking();
        activityQuery = ApplyActivityCategory(activityQuery, category);
        if (rangeStartUtc is DateTimeOffset rangeStart)
        {
            activityQuery = activityQuery.Where(
                item => item.OccurredAtUtc >= rangeStart);
        }

        int matchingEvents = await activityQuery.CountAsync(cancellationToken);
        int totalItems = recentOnly
            ? Math.Min(matchingEvents, pageSize)
            : matchingEvents;
        AuditEventEntity[] entities = await activityQuery
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip(recentOnly ? 0 : (page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        Guid[] rowSubjectIds = entities
            .Where(item => item.SubjectType == "batch-row")
            .Select(item => Guid.TryParse(item.SubjectId, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        Dictionary<Guid, (Guid BatchId, Guid? DecisionId)> rowLinks =
            await _context.BatchRows
                .AsNoTracking()
                .Where(row => rowSubjectIds.Contains(row.Id))
                .ToDictionaryAsync(
                    row => row.Id,
                    row => ValueTuple.Create(row.BatchId, row.DecisionId),
                    cancellationToken);
        ActivityRecord[] records = entities
            .Select(entity => ToActivity(entity, rowLinks))
            .ToArray();

        return new PagedResults<ActivityRecord>(
            records,
            recentOnly ? 1 : page,
            pageSize,
            totalItems);
    }

    /// <summary>
    /// Loads either today's permanent import failures or the current durable
    /// queue while keeping error text privacy-safe and result size bounded.
    /// </summary>
    /// <param name="kind">The actionable import state requested by the operator.</param>
    /// <param name="utcDayStart">The inclusive UTC-day boundary for issue rows.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="pageSize">The maximum rows returned.</param>
    /// <param name="cancellationToken">Cancels database work.</param>
    /// <returns>One predictable newest-issue or oldest-queue page.</returns>
    public async Task<PagedResults<ImportAttentionItem>> GetImportAttentionAsync(
        ImportAttentionKind kind,
        DateTimeOffset utcDayStart,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<BatchRowEntity> query = _context.BatchRows
            .AsNoTracking()
            .Include(row => row.Batch);
        query = kind switch
        {
            ImportAttentionKind.Issues => query.Where(
                row => row.Batch.CreatedAtUtc >= utcDayStart
                    && (row.Status == BatchRowStatus.ValidationFailed
                        || row.Status == BatchRowStatus.ProcessingFailed)),
            ImportAttentionKind.Queue => query.Where(
                row => row.Status == BatchRowStatus.Pending
                    || row.Status == BatchRowStatus.Processing),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The import attention kind is not supported."),
        };

        int totalItems = await query.CountAsync(cancellationToken);
        IQueryable<BatchRowEntity> ordered = kind == ImportAttentionKind.Issues
            ? query
                .OrderByDescending(row => row.Batch.CreatedAtUtc)
                .ThenBy(row => row.RowNumber)
            : query
                .OrderBy(row => row.Batch.CreatedAtUtc)
                .ThenBy(row => row.RowNumber);
        BatchRowEntity[] rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        ImportAttentionItem[] items = rows
            .Select(
                row => new ImportAttentionItem(
                    row.Id,
                    row.BatchId,
                    row.RowNumber,
                    row.Status,
                    row.ErrorCode,
                    row.ErrorMessage,
                    row.AttemptCount,
                    row.Batch.CreatedAtUtc))
            .ToArray();

        return new PagedResults<ImportAttentionItem>(
            items,
            page,
            pageSize,
            totalItems);
    }

    /// <summary>
    /// Loads one durable batch graph and its row decisions in bounded queries so
    /// the API can display outcomes without leaking persistence entities.
    /// </summary>
    /// <param name="batchId">The server-owned batch identifier.</param>
    /// <param name="cancellationToken">Cancels database queries.</param>
    /// <returns>The joined batch details or null when absent.</returns>
    public async Task<BatchDetails?> GetBatchDetailsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        BatchEntity? batch = await _context.Batches
            .AsNoTracking()
            .Include(item => item.Rows)
            .SingleOrDefaultAsync(
                item => item.Id == batchId,
                cancellationToken);
        if (batch is null)
        {
            return null;
        }

        RoutingDecisionEntity[] decisions = await _context.RoutingDecisions
            .AsNoTracking()
            .Where(decision => decision.BatchId == batchId)
            .OrderBy(decision => decision.BatchRowId)
            .ToArrayAsync(cancellationToken);
        HashSet<Guid> approvedDecisionIds = await LoadApprovedDecisionIdsAsync(
            decisions.Select(decision => decision.Id),
            cancellationToken);
        RoutingDecisionSummary[] summaries = decisions
            .Select(decision => ToSummary(decision, approvedDecisionIds))
            .ToArray();

        return new BatchDetails(
            PersistenceMapper.ToRecord(batch),
            summaries);
    }

    /// <summary>
    /// Loads a bounded newest-first batch history and computes unresolved
    /// approval holds without materializing the complete row graphs.
    /// </summary>
    public async Task<IReadOnlyList<BatchSummary>> GetRecentBatchesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        BatchEntity[] batches = await _context.Batches
            .AsNoTracking()
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        Guid[] batchIds = batches.Select(batch => batch.Id).ToArray();
        Dictionary<Guid, int> approvalCounts = await _context.RoutingDecisions
            .AsNoTracking()
            .Where(
                decision => decision.BatchId != null
                    && batchIds.Contains(decision.BatchId.Value)
                    && decision.ApprovalState
                        == ApprovalState.PendingInsuranceApproval
                    && !_context.InsuranceApprovals.Any(
                        approval => approval.DecisionId == decision.Id))
            .GroupBy(decision => decision.BatchId!.Value)
            .Select(group => new { BatchId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                item => item.BatchId,
                item => item.Count,
                cancellationToken);

        return batches
            .Select(
                batch => new BatchSummary(
                    batch.Id,
                    batch.FallbackDestinationCountry,
                    batch.Status,
                    batch.TotalRows,
                    batch.CompletedRows,
                    batch.FailedRows,
                    approvalCounts.GetValueOrDefault(batch.Id),
                    batch.CreatedAtUtc,
                    batch.CreatedBy))
            .ToArray();
    }

    /// <summary>
    /// Loads one immutable routing decision and joins its separate append-only
    /// approval evidence for an explainable operator detail.
    /// </summary>
    public async Task<RoutingDecisionDetails?> GetDecisionAsync(
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        RoutingDecisionEntity? entity = await _context.RoutingDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                decision => decision.Id == decisionId,
                cancellationToken);
        if (entity is null)
        {
            return null;
        }

        InsuranceApprovalEntity? approval = await _context.InsuranceApprovals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.DecisionId == decisionId,
                cancellationToken);
        HashSet<Guid> approved = approval is null ? [] : [decisionId];
        InsuranceApprovalSummary? evidence = approval is null
            ? null
            : new InsuranceApprovalSummary(
                approval.Id,
                approval.ApprovedBy,
                approval.ApprovedAtUtc,
                approval.CorrelationId);

        return new RoutingDecisionDetails(
            ToSummary(entity, approved),
            evidence);
    }

    /// <summary>
    /// Loads the oldest unresolved high-value decisions first so approvers work
    /// a stable bounded queue and already released decisions disappear.
    /// </summary>
    public async Task<PagedResults<RoutingDecisionSummary>>
        GetAwaitingInsuranceAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        IQueryable<RoutingDecisionEntity> awaitingQuery =
            _context.RoutingDecisions
            .AsNoTracking()
            .Where(
                decision => decision.ApprovalState
                        == ApprovalState.PendingInsuranceApproval
                    && !_context.InsuranceApprovals.Any(
                        approval => approval.DecisionId == decision.Id));
        int totalItems = await awaitingQuery.CountAsync(cancellationToken);
        RoutingDecisionEntity[] entities = await awaitingQuery
            .OrderBy(decision => decision.DecidedAtUtc)
            .ThenBy(decision => decision.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        RoutingDecisionSummary[] summaries = entities
            .Select(entity => ToSummary(entity, new HashSet<Guid>()))
            .ToArray();
        return new PagedResults<RoutingDecisionSummary>(
            summaries,
            page,
            pageSize,
            totalItems);
    }

    /// <summary>
    /// Loads approval membership for a bounded decision set to avoid one query
    /// per summary while preserving append-only approval storage.
    /// </summary>
    /// <param name="decisionIds">The bounded decision identifiers.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>A membership set of approved decision identifiers.</returns>
    private async Task<HashSet<Guid>> LoadApprovedDecisionIdsAsync(
        IEnumerable<Guid> decisionIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = decisionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        Guid[] approved = await _context.InsuranceApprovals
            .AsNoTracking()
            .Where(approval => ids.Contains(approval.DecisionId))
            .Select(approval => approval.DecisionId)
            .ToArrayAsync(cancellationToken);

        return approved.ToHashSet();
    }

    /// <summary>
    /// Applies one typed operator filter before counting or paging decisions so
    /// the result and pagination metadata always describe the same data set.
    /// </summary>
    /// <param name="query">The untracked decision query being composed.</param>
    /// <param name="filter">The allow-listed department or approval filter.</param>
    /// <returns>The constrained query without executing it.</returns>
    private IQueryable<RoutingDecisionEntity> ApplyDecisionFilter(
        IQueryable<RoutingDecisionEntity> query,
        RoutingDecisionFilter filter)
    {
        return filter switch
        {
            RoutingDecisionFilter.All => query,
            RoutingDecisionFilter.Mail => query.Where(
                decision => decision.IntendedDepartment == RoutingDepartment.Mail),
            RoutingDecisionFilter.Regular => query.Where(
                decision => decision.IntendedDepartment == RoutingDepartment.Regular),
            RoutingDecisionFilter.Heavy => query.Where(
                decision => decision.IntendedDepartment == RoutingDepartment.Heavy),
            RoutingDecisionFilter.AwaitingApproval => query.Where(
                decision => decision.ApprovalState
                        == ApprovalState.PendingInsuranceApproval
                    && !_context.InsuranceApprovals.Any(
                        approval => approval.DecisionId == decision.Id)),
            RoutingDecisionFilter.Approved => query.Where(
                decision => _context.InsuranceApprovals.Any(
                    approval => approval.DecisionId == decision.Id)),
            RoutingDecisionFilter.ApprovalNotRequired => query.Where(
                decision => decision.ApprovalState == ApprovalState.NotRequired),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filter),
                filter,
                "The routing-decision filter is not supported."),
        };
    }

    /// <summary>
    /// Applies a stable event-prefix category before activity counting and
    /// paging so operators never receive a misleading client-filtered subset.
    /// </summary>
    /// <param name="query">The untracked audit-event query being composed.</param>
    /// <param name="category">The allow-listed activity category.</param>
    /// <returns>The constrained query without executing it.</returns>
    private static IQueryable<AuditEventEntity> ApplyActivityCategory(
        IQueryable<AuditEventEntity> query,
        ActivityCategory category)
    {
        return category switch
        {
            ActivityCategory.All => query,
            ActivityCategory.Imports => query.Where(
                item => item.EventType.StartsWith("batch.")),
            ActivityCategory.Routing => query.Where(
                item => item.EventType.StartsWith("routing.")),
            ActivityCategory.Insurance => query.Where(
                item => item.EventType.StartsWith("insurance.")),
            ActivityCategory.Rules => query.Where(
                item => item.EventType.StartsWith("rule-set.")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "The activity category is not supported."),
        };
    }

    /// <summary>
    /// Maps one immutable decision entity into the public privacy-safe summary
    /// and derives approval completion from the joined append-only set.
    /// </summary>
    /// <param name="entity">The untracked immutable decision.</param>
    /// <param name="approvedDecisionIds">The joined approval membership set.</param>
    /// <returns>The operator-facing decision summary.</returns>
    private static RoutingDecisionSummary ToSummary(
        RoutingDecisionEntity entity,
        IReadOnlySet<Guid> approvedDecisionIds)
    {
        return new RoutingDecisionSummary(
            entity.Id,
            entity.WeightKilograms,
            entity.DeclaredValueEuros,
            entity.DestinationCountry,
            entity.IntendedDepartment,
            entity.ApprovalState,
            approvedDecisionIds.Contains(entity.Id),
            entity.RuleSetVersion,
            entity.MatchedRuleIds,
            entity.Reasons,
            entity.DecidedAtUtc,
            entity.CorrelationId,
            entity.BatchId,
            entity.BatchRowId);
    }

    /// <summary>
    /// Maps one audit entity and safely restores its controlled details object;
    /// invalid stored JSON is treated as an integrity failure, not hidden.
    /// </summary>
    /// <param name="entity">The untracked append-only audit entity.</param>
    /// <returns>The application audit read record.</returns>
    private static ActivityRecord ToActivity(
        AuditEventEntity entity,
        IReadOnlyDictionary<Guid, (Guid BatchId, Guid? DecisionId)> rowLinks)
    {
        IReadOnlyDictionary<string, string> details =
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                entity.DetailsJson)
            ?? throw new InvalidOperationException(
                $"Audit event {entity.Id:D} has invalid details.");

        Guid? subjectId = Guid.TryParse(entity.SubjectId, out Guid parsed)
            ? parsed
            : null;
        Guid? relatedBatchId = entity.SubjectType == "batch"
            ? subjectId
            : details.TryGetValue("batchId", out string? batchValue)
                && Guid.TryParse(batchValue, out Guid batchId)
                ? batchId
                : null;
        Guid? relatedDecisionId = entity.SubjectType == "routing-decision"
            ? subjectId
            : details.TryGetValue("decisionId", out string? decisionValue)
                && Guid.TryParse(decisionValue, out Guid decisionId)
                ? decisionId
                : null;
        if (entity.SubjectType == "batch-row"
            && subjectId is Guid rowId
            && rowLinks.TryGetValue(rowId, out var rowLink))
        {
            relatedBatchId ??= rowLink.BatchId;
            relatedDecisionId ??= rowLink.DecisionId;
        }

        return new ActivityRecord(
            entity.Id,
            entity.EventType,
            entity.SubjectType,
            entity.SubjectId,
            entity.ActorId,
            entity.CorrelationId,
            entity.OccurredAtUtc,
            details,
            relatedBatchId,
            relatedDecisionId);
    }
}
