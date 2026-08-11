using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Infrastructure;

/// <summary>
/// Supplies current system UTC time to application orchestration in production.
/// </summary>
public sealed class SystemApplicationClock : IApplicationClock
{
    /// <summary>
    /// Gets the current UTC instant from the operating system clock.
    /// </summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Generates server-owned random identifiers for durable production records.
/// </summary>
public sealed class GuidIdentifierGenerator : IIdentifierGenerator
{
    /// <summary>
    /// Creates a new non-empty version-four GUID owned by the server.
    /// </summary>
    /// <returns>A new unique record identifier.</returns>
    public Guid NewId()
    {
        return Guid.NewGuid();
    }
}
