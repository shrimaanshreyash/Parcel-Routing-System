using System.Data;
using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Persists immutable routing decisions and their audit events atomically in
/// PostgreSQL with unique idempotency enforcement.
/// </summary>
public sealed class PostgresRoutingDecisionRepository :
    IRoutingDecisionRepository
{
    private readonly ParcelRoutingDbContext _context;

    /// <summary>
    /// Creates the PostgreSQL decision repository around one scoped EF context.
    /// </summary>
    /// <param name="context">The scoped PostgreSQL persistence context.</param>
    public PostgresRoutingDecisionRepository(ParcelRoutingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Loads the original immutable decision for an idempotency key without
    /// tracking it.
    /// </summary>
    /// <param name="idempotencyKey">The normalized routing replay key.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The original decision, or null for a new request.</returns>
    public async Task<RoutingDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        RoutingDecisionEntity? entity = await _context.RoutingDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Stores the decision and audit event in one serializable transaction or
    /// returns the existing durable result for a replay.
    /// </summary>
    /// <param name="decision">The proposed immutable decision.</param>
    /// <param name="auditEvent">The corresponding audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable decision and whether it was newly created.</returns>
    public async Task<DecisionWriteResult> SaveAsync(
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        RoutingDecisionEntity? replay = await _context.RoutingDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == decision.IdempotencyKey,
                cancellationToken);

        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DecisionWriteResult(
                PersistenceMapper.ToRecord(replay),
                WasCreated: false);
        }

        _context.RoutingDecisions.Add(PersistenceMapper.ToEntity(decision));
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
            RoutingDecisionEntity? winner = await _context.RoutingDecisions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.IdempotencyKey == decision.IdempotencyKey,
                    cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new DecisionWriteResult(
                PersistenceMapper.ToRecord(winner),
                WasCreated: false);
        }

        return new DecisionWriteResult(decision, WasCreated: true);
    }
}
