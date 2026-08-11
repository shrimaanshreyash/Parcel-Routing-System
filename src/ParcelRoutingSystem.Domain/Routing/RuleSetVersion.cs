namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Identifies one immutable routing-policy version so historical decisions
/// remain explainable after future rule changes.
/// </summary>
public readonly record struct RuleSetVersion
{
    private RuleSetVersion(int value)
    {
        Value = value;
    }

    /// <summary>Gets the positive version number.</summary>
    public int Value { get; }

    /// <summary>
    /// Creates a positive immutable rule-set version.
    /// </summary>
    /// <param name="value">The version number assigned during activation.</param>
    /// <returns>A validated rule-set version.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the version is zero or negative.
    /// </exception>
    public static RuleSetVersion From(int value)
    {
        if (value <= 0)
        {
            throw new DomainValidationException(
                DomainErrorCodes.RuleSetVersionInvalid,
                "Rule-set version must be greater than zero.",
                nameof(value));
        }

        return new RuleSetVersion(value);
    }

    /// <summary>
    /// Formats the version using invariant integer representation.
    /// </summary>
    /// <returns>The version number as text.</returns>
    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
