namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Supplies server-owned identifiers so callers cannot choose database or audit
/// identities and tests can remain deterministic.
/// </summary>
public interface IIdentifierGenerator
{
    /// <summary>
    /// Creates the next unique identifier for a persisted application record.
    /// </summary>
    /// <returns>A server-owned non-empty identifier.</returns>
    Guid NewId();
}
