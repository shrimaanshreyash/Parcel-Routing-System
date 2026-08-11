namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Carries the non-personal actor and correlation identifiers supplied by a
/// future authenticated boundary into application use cases and audit events.
/// </summary>
public sealed record OperationMetadata
{
    private OperationMetadata(string actorId, string correlationId)
    {
        ActorId = actorId;
        CorrelationId = correlationId;
    }

    /// <summary>Gets the stable authenticated actor identifier.</summary>
    public string ActorId { get; }

    /// <summary>Gets the trace identifier shared by decisions and audit events.</summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Creates bounded non-personal operation metadata for traceability.
    /// </summary>
    /// <param name="actorId">The authenticated subject identifier, not a display name.</param>
    /// <param name="correlationId">The request or worker correlation identifier.</param>
    /// <returns>Validated immutable operation metadata.</returns>
    public static OperationMetadata Create(string? actorId, string? correlationId)
    {
        return new OperationMetadata(
            ApplicationGuard.RequiredText(
                actorId,
                100,
                ApplicationErrorCodes.OperationMetadataInvalid,
                "Actor identifier"),
            ApplicationGuard.RequiredText(
                correlationId,
                100,
                ApplicationErrorCodes.OperationMetadataInvalid,
                "Correlation identifier"));
    }
}
