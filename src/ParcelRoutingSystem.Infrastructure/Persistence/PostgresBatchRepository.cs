using System.Data;
using Microsoft.EntityFrameworkCore;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Routing;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Persists durable batches and uses PostgreSQL row locks with skip-locked
/// claims so processors can recover expired work without duplicate decisions.
/// </summary>
public sealed class PostgresBatchRepository : IBatchRepository
{
    private readonly ParcelRoutingDbContext _context;

    /// <summary>
    /// Creates the PostgreSQL batch repository around one scoped EF context.
    /// </summary>
    /// <param name="context">The scoped PostgreSQL persistence context.</param>
    public PostgresBatchRepository(ParcelRoutingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Loads an earlier operation result before duplicate-manifest policy is
    /// checked so transport retries remain transparent and idempotent.
    /// </summary>
    public async Task<BatchRecord?> FindBatchByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        BatchEntity? entity = await BatchGraph()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Finds the newest earlier import with identical normalized routing facts
    /// and fallback context, without exposing or storing uploaded XML.
    /// </summary>
    public async Task<BatchRecord?> FindLatestByFingerprintAsync(
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        BatchEntity? entity = await BatchGraph()
            .AsNoTracking()
            .Where(item => item.RequestFingerprint == requestFingerprint)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Stores the complete accepted batch graph and audit event in one
    /// serializable transaction or returns the original replay result.
    /// </summary>
    /// <param name="batch">The proposed privacy-minimized batch.</param>
    /// <param name="auditEvent">The creation audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>The durable batch and whether it was newly created.</returns>
    public async Task<BatchWriteResult> SaveBatchAsync(
        BatchRecord batch,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        BatchEntity? replay = await BatchGraph()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == batch.IdempotencyKey,
                cancellationToken);

        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new BatchWriteResult(
                PersistenceMapper.ToRecord(replay),
                WasCreated: false);
        }

        _context.Batches.Add(PersistenceMapper.ToEntity(batch));
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
            BatchEntity? winner = await BatchGraph()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.IdempotencyKey == batch.IdempotencyKey,
                    cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new BatchWriteResult(
                PersistenceMapper.ToRecord(winner),
                WasCreated: false);
        }

        return new BatchWriteResult(batch, WasCreated: true);
    }

    /// <summary>
    /// Claims the oldest available row under a time-bounded token using
    /// `FOR UPDATE SKIP LOCKED` so concurrent processors select different work.
    /// </summary>
    /// <param name="claimedAtUtc">The server-owned claim time.</param>
    /// <param name="leaseDuration">The bounded recovery lease duration.</param>
    /// <param name="cancellationToken">Cancels the claim transaction.</param>
    /// <returns>The claimed row and token, or null when no work is available.</returns>
    public async Task<BatchRowClaim?> ClaimNextAsync(
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        BatchRowEntity? row = await _context.BatchRows
            .FromSql(
                $"""
                 SELECT *
                 FROM parcel_batch_rows
                 WHERE status = 'Pending'
                    OR (status = 'Processing'
                        AND lease_expires_at_utc <= {claimedAtUtc})
                 ORDER BY batch_id, row_number
                 FOR UPDATE SKIP LOCKED
                 LIMIT 1
                 """)
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        Guid token = Guid.NewGuid();
        DateTimeOffset leaseExpiresAtUtc = claimedAtUtc.Add(leaseDuration);
        row.Status = BatchRowStatus.Processing;
        row.AttemptCount++;
        row.ClaimToken = token;
        row.LeaseExpiresAtUtc = leaseExpiresAtUtc;
        BatchEntity batch = await _context.Batches.SingleAsync(
            item => item.Id == row.BatchId,
            cancellationToken);
        batch.Status = BatchStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BatchRowClaim(
            token,
            PersistenceMapper.ToRecord(row),
            leaseExpiresAtUtc);
    }

    /// <summary>
    /// Commits an immutable row decision, row completion, aggregate counters,
    /// and audit event together when the supplied lease token remains current.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="decision">The immutable row decision.</param>
    /// <param name="auditEvent">The row completion audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease committed the row.</returns>
    public async Task<bool> CompleteClaimAsync(
        BatchRowClaim claim,
        RoutingDecisionRecord decision,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        BatchRowEntity? row = await FindCurrentClaimAsync(
            claim,
            cancellationToken);

        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        row.Status = BatchRowStatus.Completed;
        row.DecisionId = decision.Id;
        row.ErrorCode = null;
        row.ErrorMessage = null;
        row.ClaimToken = null;
        row.LeaseExpiresAtUtc = null;
        _context.RoutingDecisions.Add(PersistenceMapper.ToEntity(decision));
        _context.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        await _context.SaveChangesAsync(cancellationToken);
        await RefreshBatchProgressAsync(row.BatchId, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Returns a retryable row to pending state, clears its lease, and appends a
    /// deferral audit event without changing permanent failure counters.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The retryable failure category.</param>
    /// <param name="errorMessage">The safe non-personal explanation.</param>
    /// <param name="auditEvent">The row deferral audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease released the row.</returns>
    public async Task<bool> ReleaseClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        BatchRowEntity? row = await FindCurrentClaimAsync(
            claim,
            cancellationToken);

        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        row.Status = BatchRowStatus.Pending;
        row.ErrorCode = errorCode;
        row.ErrorMessage = errorMessage.Length <= 500
            ? errorMessage
            : errorMessage[..500];
        row.ClaimToken = null;
        row.LeaseExpiresAtUtc = null;
        BatchEntity batch = await _context.Batches.SingleAsync(
            item => item.Id == row.BatchId,
            cancellationToken);
        batch.Status = BatchStatus.Pending;
        _context.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Commits an isolated permanent row failure, aggregate counters, and audit
    /// event together when the supplied lease token remains current.
    /// </summary>
    /// <param name="claim">The current row lease.</param>
    /// <param name="errorCode">The stable failure category.</param>
    /// <param name="errorMessage">The safe non-personal explanation.</param>
    /// <param name="auditEvent">The row failure audit event.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>True when the current lease failed the row.</returns>
    public async Task<bool> FailClaimAsync(
        BatchRowClaim claim,
        string errorCode,
        string errorMessage,
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        BatchRowEntity? row = await FindCurrentClaimAsync(
            claim,
            cancellationToken);

        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        row.Status = BatchRowStatus.ProcessingFailed;
        row.ErrorCode = errorCode;
        row.ErrorMessage = errorMessage.Length <= 500
            ? errorMessage
            : errorMessage[..500];
        row.ClaimToken = null;
        row.LeaseExpiresAtUtc = null;
        _context.AuditEvents.Add(PersistenceMapper.ToEntity(auditEvent));
        await _context.SaveChangesAsync(cancellationToken);
        await RefreshBatchProgressAsync(row.BatchId, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Loads the current durable batch graph without tracking it.
    /// </summary>
    /// <param name="batchId">The server-owned batch identifier.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The current batch snapshot, or null when absent.</returns>
    public async Task<BatchRecord?> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        BatchEntity? entity = await BatchGraph()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == batchId,
                cancellationToken);

        return entity is null ? null : PersistenceMapper.ToRecord(entity);
    }

    /// <summary>
    /// Loads the row only when the current database token still belongs to the
    /// supplied processing attempt.
    /// </summary>
    /// <param name="claim">The claim whose ownership must be verified.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The tracked current row, or null for a stale lease.</returns>
    private Task<BatchRowEntity?> FindCurrentClaimAsync(
        BatchRowClaim claim,
        CancellationToken cancellationToken)
    {
        return _context.BatchRows.SingleOrDefaultAsync(
            row => row.Id == claim.Row.Id
                && row.Status == BatchRowStatus.Processing
                && row.ClaimToken == claim.ClaimToken,
            cancellationToken);
    }

    /// <summary>
    /// Recomputes durable counters and terminal status from row states after one
    /// row finishes inside the current transaction.
    /// </summary>
    /// <param name="batchId">The parent batch identifier.</param>
    /// <param name="cancellationToken">Cancels database queries.</param>
    private async Task RefreshBatchProgressAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        BatchEntity batch = await _context.Batches.SingleAsync(
            item => item.Id == batchId,
            cancellationToken);
        int completed = await _context.BatchRows.CountAsync(
            row => row.BatchId == batchId
                && row.Status == BatchRowStatus.Completed,
            cancellationToken);
        int failed = await _context.BatchRows.CountAsync(
            row => row.BatchId == batchId
                && (row.Status == BatchRowStatus.ValidationFailed
                    || row.Status == BatchRowStatus.ProcessingFailed),
            cancellationToken);
        int finished = completed + failed;

        batch.CompletedRows = completed;
        batch.FailedRows = failed;
        batch.Status = finished == batch.TotalRows
            ? failed == 0
                ? BatchStatus.Completed
                : BatchStatus.CompletedWithErrors
            : BatchStatus.Processing;
    }

    /// <summary>
    /// Builds the complete batch graph query required for progress inspection.
    /// </summary>
    /// <returns>A query including all durable rows.</returns>
    private IQueryable<BatchEntity> BatchGraph()
    {
        return _context.Batches.Include(item => item.Rows);
    }
}
