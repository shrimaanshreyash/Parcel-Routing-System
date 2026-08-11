using ParcelRoutingSystem.Application.Batches;

namespace ParcelRoutingSystem.Application.Imports;

/// <summary>
/// Defines the inward-owned boundary for converting one untrusted legacy XML
/// stream into ordered, privacy-minimized parcel facts.
/// </summary>
public interface IParcelManifestParser
{
    /// <summary>
    /// Parses a bounded stream without retaining recipient names, addresses, or
    /// raw XML and fails with a stable safe error for unsupported input.
    /// </summary>
    /// <param name="stream">The readable request stream positioned at its start.</param>
    /// <param name="cancellationToken">Cancels parsing and configured time limits.</param>
    /// <returns>The ordered weight, value, and optional country rows.</returns>
    Task<ParsedParcelManifest> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken);
}

/// <summary>
/// Carries the supported non-personal routing facts extracted from one manifest.
/// </summary>
public sealed record ParsedParcelManifest(
    IReadOnlyList<BatchParcelRowInput> Rows);
