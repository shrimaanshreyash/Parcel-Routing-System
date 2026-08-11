using System.Data;
using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Rules;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Persists immutable rule versions in PostgreSQL and uses serializable
/// transactions for draft creation, activation, rollback, and audit writes.
/// </summary>
public sealed class PostgresRuleSetRepository : IRuleSetRepository
{
    private readonly ParcelRoutingDbContext _context;

    /// <summary>
    /// Creates the PostgreSQL rule repository around one scoped EF context.
    /// </summary>
    /// <param name="context">The scoped PostgreSQL persistence context.</param>
    public PostgresRuleSetRepository(ParcelRoutingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Loads and validates the single active rule-set graph without tracking it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The active immutable version, or null when absent.</returns>
    public async Task<StoredRuleSet?> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        RuleSetEntity? entity = await RuleSetGraph()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Status == RuleSetLifecycleStatus.Active,
                cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Loads and validates one immutable rule-set graph without tracking it.
    /// </summary>
    /// <param name="version">The requested version.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The requested version, or null when absent.</returns>
    public async Task<StoredRuleSet?> GetVersionAsync(
        int version,
        CancellationToken cancellationToken)
    {
        RuleSetEntity? entity = await RuleSetGraph()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Version == version,
                cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Loads newest immutable versions with their complete constrained rule
    /// graphs so the administrator can monitor and choose rollback targets.
    /// </summary>
    public async Task<IReadOnlyList<StoredRuleSet>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        RuleSetEntity[] entities = await RuleSetGraph()
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return entities.Select(PersistenceMapper.ToRecord).ToArray();
    }

    /// <summary>
    /// Stores a validated draft and audit event atomically, replaying the
    /// original draft when the event idempotency key already exists.
    /// </summary>
    /// <param name="draft">The immutable validated draft.</param>
    /// <param name="auditEvent">The draft audit event.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable draft and whether it was newly created.</returns>
    public async Task<RuleSetWriteResult> SaveDraftAsync(
        StoredRuleSet draft,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        AuditEventEntity? replayEvent = await FindAuditReplayAsync(
            auditEvent.EventType,
            idempotencyKey,
            cancellationToken);

        if (replayEvent is not null)
        {
            int replayVersion = int.Parse(
                replayEvent.SubjectId,
                System.Globalization.CultureInfo.InvariantCulture);
            StoredRuleSet replay = (await GetVersionAsync(
                replayVersion,
                cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return new RuleSetWriteResult(replay, WasCreated: false);
        }

        _ = draft.Definition.ToDomain();
        _context.RuleSets.Add(PersistenceMapper.ToEntity(draft));
        _context.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            PostgresFailureClassifier.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            StoredRuleSet? winner = await GetVersionAsync(
                draft.Definition.Version,
                cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new RuleSetWriteResult(winner, WasCreated: false);
        }

        return new RuleSetWriteResult(draft, WasCreated: true);
    }

    /// <summary>
    /// Atomically activates or restores one valid version, retires the prior
    /// active version, and appends exactly one audit event.
    /// </summary>
    /// <param name="version">The version that should become active.</param>
    /// <param name="auditEvent">The activation or rollback audit event.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The resulting active version and whether state changed.</returns>
    public async Task<RuleSetActivationResult> ActivateAsync(
        int version,
        AuditEventRecord auditEvent,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        AuditEventEntity? replayEvent = await FindAuditReplayAsync(
            auditEvent.EventType,
            idempotencyKey,
            cancellationToken);

        if (replayEvent is not null)
        {
            int replayVersion = int.Parse(
                replayEvent.SubjectId,
                System.Globalization.CultureInfo.InvariantCulture);
            StoredRuleSet replay = (await GetVersionAsync(
                replayVersion,
                cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return new RuleSetActivationResult(replay, WasChanged: false);
        }

        List<RuleSetEntity> activeVersions = await RuleSetGraph()
            .Where(item => item.Status == RuleSetLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        RuleSetEntity? selected = await RuleSetGraph()
            .SingleOrDefaultAsync(
                item => item.Version == version,
                cancellationToken);

        if (selected is null)
        {
            throw new ApplicationOperationException(
                ApplicationErrorCodes.RuleSetNotFound,
                $"Rule-set version {version} does not exist.");
        }

        _ = PersistenceMapper.ToRecord(selected).Definition.ToDomain();
        foreach (RuleSetEntity active in activeVersions)
        {
            if (active.Version != selected.Version)
            {
                active.Status = RuleSetLifecycleStatus.Retired;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        selected.Status = RuleSetLifecycleStatus.Active;
        selected.ActivatedAtUtc = auditEvent.OccurredAtUtc;
        _context.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RuleSetActivationResult(
            PersistenceMapper.ToRecord(selected),
            WasChanged: true);
    }

    /// <summary>
    /// Builds the complete rule graph query required for domain reconstruction.
    /// </summary>
    /// <returns>A query including weight bands and the insurance rule.</returns>
    private IQueryable<RuleSetEntity> RuleSetGraph()
    {
        return _context.RuleSets
            .Include(item => item.WeightBands)
            .Include(item => item.InsuranceRule);
    }

    /// <summary>
    /// Finds an earlier audit event for the same operation and replay key.
    /// </summary>
    /// <param name="eventType">The allow-listed lifecycle event name.</param>
    /// <param name="idempotencyKey">The operation replay key.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The original event, or null for a new request.</returns>
    private Task<AuditEventEntity?> FindAuditReplayAsync(
        string eventType,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _context.AuditEvents.SingleOrDefaultAsync(
            item => item.EventType == eventType
                && item.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }
}
