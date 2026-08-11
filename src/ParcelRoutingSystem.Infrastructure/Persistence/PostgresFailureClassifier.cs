using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ParcelRoutingSystem.Infrastructure.Persistence;

/// <summary>
/// Classifies PostgreSQL failures that represent expected idempotency races so
/// repositories can replay the winning transaction without hiding other errors.
/// </summary>
internal static class PostgresFailureClassifier
{
    /// <summary>
    /// Determines whether an EF update failed specifically because a PostgreSQL
    /// unique constraint selected another concurrent request as the winner.
    /// </summary>
    /// <param name="exception">The EF persistence failure to inspect.</param>
    /// <returns>True only for PostgreSQL SQLSTATE 23505.</returns>
    internal static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}
