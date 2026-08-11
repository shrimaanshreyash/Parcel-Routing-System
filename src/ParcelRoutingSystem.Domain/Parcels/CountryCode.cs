namespace ParcelRoutingSystem.Domain.Parcels;

/// <summary>
/// Represents an explicitly supplied ISO 3166-1 alpha-2 destination country.
/// It never infers a country from an address, city, postal code, or filename.
/// </summary>
public readonly record struct CountryCode
{
    private CountryCode(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the normalized uppercase ISO alpha-2 code.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a destination country from an assigned ISO alpha-2 code.
    /// </summary>
    /// <param name="alpha2">The operator- or source-supplied country code.</param>
    /// <returns>A normalized immutable country code.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the country is missing, malformed, or unassigned.
    /// </exception>
    public static CountryCode FromAlpha2(string? alpha2)
    {
        if (string.IsNullOrWhiteSpace(alpha2))
        {
            throw new DomainValidationException(
                DomainErrorCodes.CountryRequired,
                "Destination country is required.",
                nameof(alpha2));
        }

        string normalized = alpha2.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || !IsoCountryCodes.Contains(normalized))
        {
            throw new DomainValidationException(
                DomainErrorCodes.CountryInvalid,
                "Destination country must be an assigned ISO 3166-1 alpha-2 code.",
                nameof(alpha2));
        }

        return new CountryCode(normalized);
    }

    /// <summary>
    /// Returns the normalized code for logs and explanations that do not include
    /// recipient personal data.
    /// </summary>
    /// <returns>The uppercase alpha-2 code.</returns>
    public override string ToString()
    {
        return Value;
    }
}
