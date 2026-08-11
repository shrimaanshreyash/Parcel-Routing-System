using System.Text.Json;
using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;
using ParcelRoutingSystem.Application.Rules;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Translates between application records and EF Core entities so database
/// shapes never leak into use-case contracts.
/// </summary>
internal static class PersistenceMapper
{
    /// <summary>
    /// Creates an EF graph for one immutable rule-set version.
    /// </summary>
    /// <param name="stored">The validated application rule-set record.</param>
    /// <returns>A new persistence graph ready to add.</returns>
    internal static RuleSetEntity ToEntity(StoredRuleSet stored)
    {
        var entity = new RuleSetEntity
        {
            Version = stored.Definition.Version,
            Status = stored.Status,
            CreatedAtUtc = stored.CreatedAtUtc,
            CreatedBy = stored.CreatedBy,
            ActivatedAtUtc = stored.ActivatedAtUtc,
        };
        entity.WeightBands = stored.Definition.WeightBands
            .Select(
                band => new WeightBandRuleEntity
                {
                    RuleSetVersion = stored.Definition.Version,
                    RuleId = band.RuleId,
                    Priority = band.Priority,
                    LowerBoundExclusive = band.LowerBoundExclusive,
                    UpperBoundInclusive = band.UpperBoundInclusive,
                    Department = band.Department,
                    RuleSet = entity,
                })
            .ToList();
        entity.InsuranceRule = new InsuranceRuleEntity
        {
            RuleSetVersion = stored.Definition.Version,
            RuleId = stored.Definition.InsuranceRule.RuleId,
            Priority = stored.Definition.InsuranceRule.Priority,
            ThresholdExclusiveEuros =
                stored.Definition.InsuranceRule.ThresholdExclusiveEuros,
            RuleSet = entity,
        };

        return entity;
    }

    /// <summary>
    /// Reconstructs an application rule-set record from a fully included EF
    /// graph and validates all required children are present.
    /// </summary>
    /// <param name="entity">The tracked or untracked persistence graph.</param>
    /// <returns>The immutable application rule-set record.</returns>
    internal static StoredRuleSet ToRecord(RuleSetEntity entity)
    {
        InsuranceRuleEntity insurance = entity.InsuranceRule
            ?? throw new InvalidOperationException(
                $"Rule-set version {entity.Version} has no insurance rule.");
        WeightBandDefinition[] bands = entity.WeightBands
            .OrderBy(item => item.LowerBoundExclusive)
            .Select(
                item => new WeightBandDefinition(
                    item.RuleId,
                    item.Priority,
                    item.LowerBoundExclusive,
                    item.UpperBoundInclusive,
                    item.Department))
            .ToArray();
        var definition = new RuleSetDefinition(
            entity.Version,
            bands,
            new InsuranceRuleDefinition(
                insurance.RuleId,
                insurance.Priority,
                insurance.ThresholdExclusiveEuros));
        _ = definition.ToDomain();

        return new StoredRuleSet(
            definition,
            entity.Status,
            entity.CreatedAtUtc,
            entity.CreatedBy,
            entity.ActivatedAtUtc);
    }

    /// <summary>
    /// Creates an EF entity for one immutable routing decision.
    /// </summary>
    /// <param name="record">The application decision record.</param>
    /// <returns>A new decision entity ready to add.</returns>
    internal static RoutingDecisionEntity ToEntity(RoutingDecisionRecord record)
    {
        return new RoutingDecisionEntity
        {
            Id = record.Id,
            IdempotencyKey = record.IdempotencyKey,
            RequestFingerprint = record.RequestFingerprint,
            WeightKilograms = record.WeightKilograms,
            DeclaredValueEuros = record.DeclaredValueEuros,
            DestinationCountry = record.DestinationCountry,
            IntendedDepartment = record.IntendedDepartment,
            ApprovalState = record.ApprovalState,
            RuleSetVersion = record.RuleSetVersion,
            MatchedRuleIds = record.MatchedRuleIds.ToArray(),
            Reasons = record.Reasons.ToArray(),
            DecidedAtUtc = record.DecidedAtUtc,
            CorrelationId = record.CorrelationId,
            BatchId = record.BatchId,
            BatchRowId = record.BatchRowId,
        };
    }

    /// <summary>
    /// Reconstructs an immutable application decision from database state.
    /// </summary>
    /// <param name="entity">The tracked or untracked decision entity.</param>
    /// <returns>The immutable application decision record.</returns>
    internal static RoutingDecisionRecord ToRecord(RoutingDecisionEntity entity)
    {
        return new RoutingDecisionRecord(
            entity.Id,
            entity.IdempotencyKey,
            entity.RequestFingerprint,
            entity.WeightKilograms,
            entity.DeclaredValueEuros,
            entity.DestinationCountry,
            entity.IntendedDepartment,
            entity.ApprovalState,
            entity.RuleSetVersion,
            entity.MatchedRuleIds,
            entity.Reasons,
            entity.DecidedAtUtc,
            entity.CorrelationId,
            entity.BatchId,
            entity.BatchRowId);
    }

    /// <summary>
    /// Creates an append-only EF audit entity with deterministic JSON details.
    /// </summary>
    /// <param name="record">The privacy-safe application audit record.</param>
    /// <returns>A new audit entity ready to add.</returns>
    internal static AuditEventEntity ToEntity(AuditEventRecord record)
    {
        var orderedDetails = new SortedDictionary<string, string>(
            StringComparer.Ordinal);
        foreach ((string name, string value) in record.Details)
        {
            orderedDetails.Add(name, value);
        }

        return new AuditEventEntity
        {
            Id = record.Id,
            EventType = record.EventType,
            SubjectType = record.SubjectType,
            SubjectId = record.SubjectId,
            ActorId = record.ActorId,
            CorrelationId = record.CorrelationId,
            IdempotencyKey = record.IdempotencyKey,
            OccurredAtUtc = record.OccurredAtUtc,
            DetailsJson = JsonSerializer.Serialize(orderedDetails),
        };
    }

    /// <summary>
    /// Creates an append-only EF approval entity.
    /// </summary>
    /// <param name="record">The application approval record.</param>
    /// <returns>A new approval entity ready to add.</returns>
    internal static InsuranceApprovalEntity ToEntity(InsuranceApprovalRecord record)
    {
        return new InsuranceApprovalEntity
        {
            Id = record.Id,
            DecisionId = record.DecisionId,
            IdempotencyKey = record.IdempotencyKey,
            ApprovedBy = record.ApprovedBy,
            ApprovedAtUtc = record.ApprovedAtUtc,
            CorrelationId = record.CorrelationId,
        };
    }

    /// <summary>
    /// Reconstructs an immutable application approval from database state.
    /// </summary>
    /// <param name="entity">The tracked or untracked approval entity.</param>
    /// <returns>The application approval record.</returns>
    internal static InsuranceApprovalRecord ToRecord(
        InsuranceApprovalEntity entity)
    {
        return new InsuranceApprovalRecord(
            entity.Id,
            entity.DecisionId,
            entity.IdempotencyKey,
            entity.ApprovedBy,
            entity.ApprovedAtUtc,
            entity.CorrelationId);
    }

    /// <summary>
    /// Creates an EF batch graph including every independently durable row.
    /// </summary>
    /// <param name="record">The application batch snapshot.</param>
    /// <returns>A new batch graph ready to add.</returns>
    internal static BatchEntity ToEntity(BatchRecord record)
    {
        var entity = new BatchEntity
        {
            Id = record.Id,
            IdempotencyKey = record.IdempotencyKey,
            RequestFingerprint = record.RequestFingerprint,
            FallbackDestinationCountry = record.FallbackDestinationCountry,
            Status = record.Status,
            TotalRows = record.TotalRows,
            CompletedRows = record.CompletedRows,
            FailedRows = record.FailedRows,
            CreatedAtUtc = record.CreatedAtUtc,
            CreatedBy = record.CreatedBy,
        };
        entity.Rows = record.Rows
            .Select(
                row => new BatchRowEntity
                {
                    Id = row.Id,
                    BatchId = row.BatchId,
                    RowNumber = row.RowNumber,
                    WeightKilograms = row.WeightKilograms,
                    DeclaredValueEuros = row.DeclaredValueEuros,
                    DestinationCountry = row.DestinationCountry,
                    CountrySource = row.CountrySource,
                    Status = row.Status,
                    ErrorCode = row.ErrorCode,
                    ErrorMessage = row.ErrorMessage,
                    AttemptCount = row.AttemptCount,
                    DecisionId = row.DecisionId,
                    Batch = entity,
                })
            .ToList();

        return entity;
    }

    /// <summary>
    /// Reconstructs the current application batch snapshot from a fully included
    /// persistence graph.
    /// </summary>
    /// <param name="entity">The tracked or untracked batch graph.</param>
    /// <returns>The immutable application batch snapshot.</returns>
    internal static BatchRecord ToRecord(BatchEntity entity)
    {
        BatchRowRecord[] rows = entity.Rows
            .OrderBy(row => row.RowNumber)
            .Select(ToRecord)
            .ToArray();

        return new BatchRecord(
            entity.Id,
            entity.IdempotencyKey,
            entity.RequestFingerprint,
            entity.FallbackDestinationCountry,
            entity.Status,
            entity.TotalRows,
            entity.CompletedRows,
            entity.FailedRows,
            entity.CreatedAtUtc,
            entity.CreatedBy,
            rows);
    }

    /// <summary>
    /// Reconstructs one application batch-row snapshot without exposing lease
    /// internals outside the repository.
    /// </summary>
    /// <param name="entity">The tracked or untracked row entity.</param>
    /// <returns>The immutable application row record.</returns>
    internal static BatchRowRecord ToRecord(BatchRowEntity entity)
    {
        return new BatchRowRecord(
            entity.Id,
            entity.BatchId,
            entity.RowNumber,
            entity.WeightKilograms,
            entity.DeclaredValueEuros,
            entity.DestinationCountry,
            entity.CountrySource,
            entity.Status,
            entity.ErrorCode,
            entity.ErrorMessage,
            entity.AttemptCount,
            entity.DecisionId);
    }
}
