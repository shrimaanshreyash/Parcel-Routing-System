using System.Globalization;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Operations;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Api.Contracts;

/// <summary>
/// Converts application records into version-stable HTTP contracts without
/// leaking EF entities, request fingerprints, or idempotency keys.
/// </summary>
public static class ApiContractMapper
{
    /// <summary>
    /// Maps a route use-case result and preserves whether persistence replayed
    /// the original durable decision.
    /// </summary>
    /// <param name="result">The application routing result.</param>
    /// <returns>The public routing response.</returns>
    public static RouteParcelResponse ToResponse(RouteParcelResult result)
    {
        return new RouteParcelResponse(
            ToDecision(result.Decision, isInsuranceApproved: false),
            result.WasReplay);
    }

    /// <summary>
    /// Maps an immutable write record when approval completion is not joined in
    /// the current use-case response.
    /// </summary>
    /// <param name="record">The immutable application decision.</param>
    /// <param name="isInsuranceApproved">Whether an append-only approval exists.</param>
    /// <returns>The public explainable decision.</returns>
    public static RoutingDecisionResponse ToDecision(
        RoutingDecisionRecord record,
        bool isInsuranceApproved)
    {
        return new RoutingDecisionResponse(
            record.Id,
            record.WeightKilograms,
            record.DeclaredValueEuros,
            record.DestinationCountry,
            record.IntendedDepartment.ToString(),
            record.ApprovalState.ToString(),
            isInsuranceApproved,
            record.RuleSetVersion,
            record.MatchedRuleIds,
            record.Reasons,
            record.DecidedAtUtc,
            record.CorrelationId,
            record.BatchId);
    }

    /// <summary>
    /// Maps a joined read summary including append-only approval completion.
    /// </summary>
    /// <param name="record">The privacy-safe application decision summary.</param>
    /// <returns>The public explainable decision.</returns>
    public static RoutingDecisionResponse ToDecision(
        RoutingDecisionSummary record)
    {
        return new RoutingDecisionResponse(
            record.Id,
            record.WeightKilograms,
            record.DeclaredValueEuros,
            record.DestinationCountry,
            record.IntendedDepartment.ToString(),
            record.ApprovalState.ToString(),
            record.IsInsuranceApproved,
            record.RuleSetVersion,
            record.MatchedRuleIds,
            record.Reasons,
            record.DecidedAtUtc,
            record.CorrelationId,
            record.BatchId);
    }

    /// <summary>
    /// Maps current overview counters and newest immutable decisions.
    /// </summary>
    /// <param name="overview">The application operations overview.</param>
    /// <returns>The public overview response.</returns>
    public static OperationsOverviewResponse ToResponse(
        OperationsOverview overview)
    {
        return new OperationsOverviewResponse(
            overview.TotalDecisions,
            overview.ProcessedToday,
            overview.AwaitingInsuranceApproval,
            overview.ImportIssues,
            overview.PendingBatchRows,
            overview.DecisionRange.ToString(),
            overview.DecisionFilter.ToString(),
            ToResponse(overview.DecisionHistory, ToDecision));
    }

    /// <summary>
    /// Maps one privacy-safe audit record without exposing its idempotency key.
    /// </summary>
    /// <param name="record">The application audit read record.</param>
    /// <returns>The public activity response.</returns>
    public static ActivityResponse ToResponse(ActivityRecord record)
    {
        return new ActivityResponse(
            record.Id,
            record.EventType,
            record.SubjectType,
            record.SubjectId,
            record.ActorId,
            record.CorrelationId,
            record.OccurredAtUtc,
            record.Details,
            record.RelatedBatchId,
            record.RelatedDecisionId);
    }

    /// <summary>
    /// Maps one actionable import row while retaining only safe error text and
    /// opaque durable identifiers.
    /// </summary>
    /// <param name="item">The privacy-safe application attention record.</param>
    /// <returns>The stable HTTP attention contract.</returns>
    public static ImportAttentionResponse ToResponse(ImportAttentionItem item)
    {
        return new ImportAttentionResponse(
            item.RowId,
            item.BatchId,
            item.RowNumber,
            item.Status.ToString(),
            item.ErrorCode,
            item.ErrorMessage,
            item.AttemptCount,
            item.BatchCreatedAtUtc);
    }

    /// <summary>
    /// Maps one bounded application page while preserving server-owned totals
    /// and applying the supplied privacy-safe item mapper.
    /// </summary>
    /// <typeparam name="TSource">The application item type.</typeparam>
    /// <typeparam name="TTarget">The HTTP contract item type.</typeparam>
    /// <param name="page">The bounded application page.</param>
    /// <param name="map">The explicit item contract mapper.</param>
    /// <returns>The version-stable HTTP page.</returns>
    public static PagedResponse<TTarget> ToResponse<TSource, TTarget>(
        PagedResults<TSource> page,
        Func<TSource, TTarget> map)
    {
        return new PagedResponse<TTarget>(
            page.Items.Select(map).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalItems,
            page.TotalPages);
    }

    /// <summary>
    /// Maps one active constrained rule set into readable rows that remain
    /// data-driven rather than hardcoded in the browser.
    /// </summary>
    /// <param name="stored">The validated active immutable rule set.</param>
    /// <returns>The public active-policy response.</returns>
    public static ActiveRuleSetResponse ToResponse(StoredRuleSet stored)
    {
        WeightBandDefinition mailBand = stored.Definition.WeightBands.Single(
            band => band.Department == RoutingDepartment.Mail);
        WeightBandDefinition regularBand = stored.Definition.WeightBands.Single(
            band => band.Department == RoutingDepartment.Regular);
        ActiveRuleResponse[] weightRules = stored.Definition.WeightBands
            .OrderBy(band => band.Priority)
            .Select(
                band => new ActiveRuleResponse(
                    band.RuleId,
                    "Weight",
                    FormatWeightCondition(band),
                    $"{band.Department} department",
                    band.Priority))
            .ToArray();
        InsuranceRuleDefinition insurance = stored.Definition.InsuranceRule;
        var insuranceRule = new ActiveRuleResponse(
            insurance.RuleId,
            "Declared value",
            $"Greater than EUR {insurance.ThresholdExclusiveEuros.ToString("N2", CultureInfo.InvariantCulture)}",
            "Insurance approval required",
            insurance.Priority);

        return new ActiveRuleSetResponse(
            stored.Definition.Version,
            stored.Status.ToString(),
            stored.CreatedAtUtc,
            stored.CreatedBy,
            stored.ActivatedAtUtc,
            mailBand.UpperBoundInclusive!.Value,
            regularBand.UpperBoundInclusive!.Value,
            insurance.ThresholdExclusiveEuros,
            [.. weightRules, insuranceRule]);
    }

    /// <summary>
    /// Maps one joined decision detail and optional append-only approval
    /// evidence into a stable HTTP response.
    /// </summary>
    public static RoutingDecisionDetailsResponse ToResponse(
        RoutingDecisionDetails details)
    {
        InsuranceApprovalEvidenceResponse? approval = details.Approval is null
            ? null
            : new InsuranceApprovalEvidenceResponse(
                details.Approval.Id,
                details.Approval.ApprovedBy,
                details.Approval.ApprovedAtUtc,
                details.Approval.CorrelationId);
        return new RoutingDecisionDetailsResponse(
            ToDecision(details.Decision),
            approval);
    }

    /// <summary>
    /// Maps one lightweight durable import summary without loading its rows.
    /// </summary>
    public static BatchSummaryResponse ToResponse(BatchSummary summary)
    {
        return new BatchSummaryResponse(
            summary.Id,
            summary.FallbackDestinationCountry,
            summary.Status.ToString(),
            summary.TotalRows,
            summary.CompletedRows,
            summary.FailedRows,
            summary.AwaitingInsuranceApproval,
            summary.CreatedAtUtc,
            summary.CreatedBy);
    }

    /// <summary>
    /// Maps durable batch progress and joins decisions by row identifier while
    /// keeping row order stable for operator reconciliation.
    /// </summary>
    /// <param name="details">The application batch and decision read model.</param>
    /// <param name="wasCreated">Whether this response follows a new import.</param>
    /// <returns>The public batch polling response.</returns>
    public static BatchResponse ToResponse(
        BatchDetails details,
        bool wasCreated = false)
    {
        IReadOnlyDictionary<Guid, RoutingDecisionSummary> decisionsByRow =
            details.Decisions
                .Where(decision => decision.BatchRowId is not null)
                .ToDictionary(
                    decision => decision.BatchRowId!.Value);
        BatchRowResponse[] rows = details.Batch.Rows
            .OrderBy(row => row.RowNumber)
            .Select(
                row => new BatchRowResponse(
                    row.Id,
                    row.RowNumber,
                    row.WeightKilograms,
                    row.DeclaredValueEuros,
                    row.DestinationCountry,
                    row.CountrySource.ToString(),
                    row.Status.ToString(),
                    row.ErrorCode,
                    row.ErrorMessage,
                    row.AttemptCount,
                    decisionsByRow.TryGetValue(row.Id, out RoutingDecisionSummary? decision)
                        ? ToDecision(decision)
                        : null))
            .ToArray();

        return new BatchResponse(
            details.Batch.Id,
            wasCreated,
            details.Batch.FallbackDestinationCountry,
            details.Batch.Status.ToString(),
            details.Batch.TotalRows,
            details.Batch.CompletedRows,
            details.Batch.FailedRows,
            details.Batch.CreatedAtUtc,
            rows);
    }

    /// <summary>
    /// Formats one typed lower-exclusive and optional upper-inclusive band into
    /// a plain-language API condition.
    /// </summary>
    /// <param name="band">The constrained weight band.</param>
    /// <returns>The readable deterministic condition.</returns>
    private static string FormatWeightCondition(WeightBandDefinition band)
    {
        string lower = band.LowerBoundExclusive.ToString(
            "G29",
            CultureInfo.InvariantCulture);
        return band.UpperBoundInclusive is decimal upper
            ? $"{lower} kg < weight <= {upper.ToString("G29", CultureInfo.InvariantCulture)} kg"
            : $"Weight > {lower} kg";
    }
}
