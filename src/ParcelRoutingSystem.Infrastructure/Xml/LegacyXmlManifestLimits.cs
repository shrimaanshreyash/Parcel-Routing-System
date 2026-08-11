namespace ParcelRoutingSystem.Infrastructure.Xml;

/// <summary>
/// Defines hard parser limits that bound XML memory, work, and processing time
/// independently of HTTP request-size enforcement.
/// </summary>
public sealed record LegacyXmlManifestLimits
{
    /// <summary>Gets the default maximum number of supported parcel rows.</summary>
    public const int DefaultMaximumRows = 10_000;

    /// <summary>Gets the default maximum number of XML characters.</summary>
    public const long DefaultMaximumCharacters = 2_000_000;

    /// <summary>Gets the default maximum parser duration.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Creates validated limits or throws during startup rather than accepting
    /// an unsafe or unusable parser configuration.
    /// </summary>
    /// <param name="maximumRows">The positive row limit.</param>
    /// <param name="maximumCharacters">The positive document-character limit.</param>
    /// <param name="timeout">The positive parsing timeout.</param>
    public LegacyXmlManifestLimits(
        int maximumRows = DefaultMaximumRows,
        long maximumCharacters = DefaultMaximumCharacters,
        TimeSpan? timeout = null)
    {
        if (maximumRows is < 1 or > DefaultMaximumRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRows),
                $"Maximum rows must be between 1 and {DefaultMaximumRows}.");
        }

        if (maximumCharacters is < 1 or > DefaultMaximumCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                $"Maximum characters must be between 1 and {DefaultMaximumCharacters}.");
        }

        TimeSpan resolvedTimeout = timeout ?? DefaultTimeout;
        if (resolvedTimeout <= TimeSpan.Zero || resolvedTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Parser timeout must be positive and no longer than one minute.");
        }

        MaximumRows = maximumRows;
        MaximumCharacters = maximumCharacters;
        Timeout = resolvedTimeout;
    }

    /// <summary>Gets the largest accepted parcel count.</summary>
    public int MaximumRows { get; }

    /// <summary>Gets the largest accepted XML character count.</summary>
    public long MaximumCharacters { get; }

    /// <summary>Gets the maximum allowed parser duration.</summary>
    public TimeSpan Timeout { get; }
}
