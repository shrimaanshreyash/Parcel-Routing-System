using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Carries one parsed, privacy-minimized batch row before domain validation.
/// Recipient names and addresses are intentionally absent.
/// </summary>
public sealed record BatchParcelRowInput(
    decimal WeightKilograms,
    decimal DeclaredValueEuros,
    string? DestinationCountry = null,
    string? ValidationErrorCode = null,
    string? ValidationErrorMessage = null);

/// <summary>
/// Carries an idempotent batch-creation request after a future parser has
/// supplied ordered weight/value rows and any explicit fallback country.
/// </summary>
public sealed record CreateBatchCommand(
    string IdempotencyKey,
    string? FallbackDestinationCountry,
    IReadOnlyList<BatchParcelRowInput> Rows,
    OperationMetadata Metadata,
    bool AllowDuplicate = false);
