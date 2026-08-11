using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Routing;

/// <summary>
/// Carries the privacy-minimized facts required to route one parcel plus the
/// metadata needed for idempotency and auditing.
/// </summary>
public sealed record RouteParcelCommand(
    string IdempotencyKey,
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string DestinationCountry,
    IReadOnlyDictionary<string, string>? AdditionalAttributes,
    OperationMetadata Metadata,
    Guid? BatchId = null,
    Guid? BatchRowId = null);
