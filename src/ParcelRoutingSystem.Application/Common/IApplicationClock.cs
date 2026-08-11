namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Supplies current UTC time to orchestration while keeping use cases and
/// domain evaluation deterministic in tests.
/// </summary>
public interface IApplicationClock
{
    /// <summary>
    /// Gets the current UTC timestamp for one application operation.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
