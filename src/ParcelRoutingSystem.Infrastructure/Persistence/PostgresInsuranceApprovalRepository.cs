using System.Data;
using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Approvals;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Persists append-only insurance approvals with decision-state checks and audit
/// evidence inside one PostgreSQL transaction.
/// </summary>
public sealed class PostgresInsuranceApprovalRepository :
    IInsuranceApprovalRepository
{
    private readonly ParcelRoutingDbContext _context;

    /// <summary>
    /// Creates the PostgreSQL approval repository around one scoped EF context.
    /// </summary>
    /// <param name="context">The scoped PostgreSQL persistence context.</param>
    public PostgresInsuranceApprovalRepository(ParcelRoutingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Verifies the immutable decision requires approval and atomically stores or
    /// replays one append-only approval with its audit event.
    /// </summary>
    /// <param name="approval">The proposed approval.</param>
    /// <param name="auditEvent">The corresponding audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The explicit approval outcome.</returns>
    public async Task<InsuranceApprovalWriteResult> ApproveAsync(
        InsuranceApprovalRecord approval,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        InsuranceApprovalEntity? replay = await _context.InsuranceApprovals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == approval.IdempotencyKey,
                cancellationToken);

        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.Replayed,
                PersistenceMapper.ToRecord(replay));
        }

        RoutingDecisionEntity? decision = await _context.RoutingDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == approval.DecisionId,
                cancellationToken);
        if (decision is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.DecisionNotFound,
                Approval: null);
        }

        if (decision.ApprovalState != ApprovalState.PendingInsuranceApproval)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.ApprovalNotRequired,
                Approval: null);
        }

        InsuranceApprovalEntity? existingForDecision =
            await _context.InsuranceApprovals
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.DecisionId == approval.DecisionId,
                    cancellationToken);
        if (existingForDecision is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.Replayed,
                PersistenceMapper.ToRecord(existingForDecision));
        }

        _context.InsuranceApprovals.Add(PersistenceMapper.ToEntity(approval));
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
            InsuranceApprovalEntity? winner = await _context.InsuranceApprovals
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.IdempotencyKey == approval.IdempotencyKey
                        || item.DecisionId == approval.DecisionId,
                    cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new InsuranceApprovalWriteResult(
                InsuranceApprovalWriteStatus.Replayed,
                PersistenceMapper.ToRecord(winner));
        }

        return new InsuranceApprovalWriteResult(
            InsuranceApprovalWriteStatus.Created,
            approval);
    }
}
