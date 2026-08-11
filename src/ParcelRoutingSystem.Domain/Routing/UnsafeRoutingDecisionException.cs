namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Represents a fail-closed routing attempt where a validated policy still
/// failed to select exactly one department.
/// </summary>
public sealed class UnsafeRoutingDecisionException : InvalidOperationException
{
    /// <summary>
    /// Creates a fail-closed decision error rather than allowing the domain to
    /// guess a destination.
    /// </summary>
    /// <param name="message">The diagnostic reason routing was stopped.</param>
    internal UnsafeRoutingDecisionException(string message)
        : base(message)
    {
    }
}
