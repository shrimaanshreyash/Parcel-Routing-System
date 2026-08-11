namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Represents a stable uppercase rule identifier retained in every routing
/// decision for audit and regression analysis.
/// </summary>
public readonly record struct RuleId
{
    private RuleId(string value)
    {
        Value = value;
    }

    /// <summary>Gets the normalized stable identifier.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a normalized rule identifier containing uppercase letters,
    /// digits, and single separating hyphens.
    /// </summary>
    /// <param name="value">The human-assigned stable identifier.</param>
    /// <returns>A validated immutable rule identifier.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the identifier is blank or malformed.
    /// </exception>
    public static RuleId From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException(
                DomainErrorCodes.RuleIdInvalid,
                "Rule identifier is required.",
                nameof(value));
        }

        string normalized = value.Trim().ToUpperInvariant();
        if (!IsWellFormed(normalized))
        {
            throw new DomainValidationException(
                DomainErrorCodes.RuleIdInvalid,
                "Rule identifier may contain uppercase letters, digits, and single hyphens only.",
                nameof(value));
        }

        return new RuleId(normalized);
    }

    /// <summary>
    /// Determines whether a normalized identifier has valid characters and no
    /// leading, trailing, or repeated hyphen.
    /// </summary>
    /// <param name="value">The normalized identifier candidate.</param>
    /// <returns><see langword="true"/> when the identifier is stable and safe.</returns>
    private static bool IsWellFormed(string value)
    {
        bool previousWasHyphen = true;

        foreach (char character in value)
        {
            bool isHyphen = character == '-';
            bool isAllowed = isHyphen
                || character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9';

            if (!isAllowed || (isHyphen && previousWasHyphen))
            {
                return false;
            }

            previousWasHyphen = isHyphen;
        }

        return !previousWasHyphen;
    }

    /// <summary>
    /// Returns the normalized identifier for decision explanations and storage.
    /// </summary>
    /// <returns>The stable rule identifier.</returns>
    public override string ToString()
    {
        return Value;
    }
}
