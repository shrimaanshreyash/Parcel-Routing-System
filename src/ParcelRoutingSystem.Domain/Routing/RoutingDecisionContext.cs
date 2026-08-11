namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Carries caller-owned time and correlation metadata into a pure routing
/// decision without allowing the domain to read a clock or generate identifiers.
/// </summary>
public sealed class RoutingDecisionContext
{
    private RoutingDecisionContext(
        DateTimeOffset decidedAtUtc,
        string correlationId)
    {
        DecidedAtUtc = decidedAtUtc;
        CorrelationId = correlationId;
    }

    /// <summary>Gets the caller-supplied decision time normalized to UTC.</summary>
    public DateTimeOffset DecidedAtUtc { get; }

    /// <summary>Gets the trimmed trace identifier for this decision.</summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Creates validated decision metadata supplied by an outer application use
    /// case.
    /// </summary>
    /// <param name="decidedAt">The caller's non-default decision timestamp.</param>
    /// <param name="correlationId">The non-empty trace identifier.</param>
    /// <returns>Normalized immutable decision metadata.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the timestamp is default or the correlation identifier is
    /// missing.
    /// </exception>
    public static RoutingDecisionContext Create(
        DateTimeOffset decidedAt,
        string? correlationId)
    {
        if (decidedAt == default)
        {
            throw new DomainValidationException(
                DomainErrorCodes.DecisionContextInvalid,
                "Decision timestamp is required.",
                nameof(decidedAt));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new DomainValidationException(
                DomainErrorCodes.DecisionContextInvalid,
                "Correlation identifier is required.",
                nameof(correlationId));
        }

        return new RoutingDecisionContext(
            decidedAt.ToUniversalTime(),
            correlationId.Trim());
    }
}
