using System.Globalization;

namespace ParcelRoutingSystem.Domain.Parcels;

/// <summary>
/// Represents a validated positive parcel weight in kilograms. Keeping the
/// invariant here prevents invalid weights from entering any routing rule.
/// </summary>
public readonly record struct Weight
{
    private Weight(decimal kilograms)
    {
        Kilograms = kilograms;
    }

    /// <summary>
    /// Gets the exact decimal weight used for deterministic threshold
    /// comparisons without floating-point rounding.
    /// </summary>
    public decimal Kilograms { get; }

    /// <summary>
    /// Creates a positive parcel weight.
    /// </summary>
    /// <param name="kilograms">The parcel weight expressed in kilograms.</param>
    /// <returns>A validated immutable weight.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the value is zero or negative.
    /// </exception>
    public static Weight FromKilograms(decimal kilograms)
    {
        if (kilograms <= 0m)
        {
            throw new DomainValidationException(
                DomainErrorCodes.WeightMustBePositive,
                "Parcel weight must be greater than zero kilograms.",
                nameof(kilograms));
        }

        return new Weight(kilograms);
    }

    /// <summary>
    /// Formats the weight invariantly for stable decision explanations.
    /// </summary>
    /// <returns>The exact weight followed by the kilogram unit.</returns>
    public override string ToString()
    {
        return $"{Kilograms.ToString("0.############################", CultureInfo.InvariantCulture)} kg";
    }
}
