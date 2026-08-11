namespace ParcelRoutingSystem.Application.Batches;

/// <summary>
/// Carries the safe prior-batch facts needed to ask an operator before a
/// deliberate duplicate import. The normalized fingerprint remains private.
/// </summary>
public sealed class DuplicateManifestException : InvalidOperationException
{
    /// <summary>
    /// Creates a duplicate warning tied to the earlier durable batch.
    /// </summary>
    /// <param name="previousBatchId">The earlier batch that matched.</param>
    /// <param name="previousImportedAtUtc">When the earlier batch was accepted.</param>
    public DuplicateManifestException(
        Guid previousBatchId,
        DateTimeOffset previousImportedAtUtc)
        : base("This manifest and fallback country were imported previously.")
    {
        PreviousBatchId = previousBatchId;
        PreviousImportedAtUtc = previousImportedAtUtc.ToUniversalTime();
    }

    /// <summary>Gets the earlier batch that the operator can review.</summary>
    public Guid PreviousBatchId { get; }

    /// <summary>Gets the UTC acceptance time of the earlier batch.</summary>
    public DateTimeOffset PreviousImportedAtUtc { get; }
}
