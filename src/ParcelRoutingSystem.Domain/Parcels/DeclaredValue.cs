using System.Globalization;

namespace ParcelRoutingSystem.Domain.Parcels;

/// <summary>
/// Represents a parcel's validated non-negative declared value in euros. The
/// assignment uses one currency, so no unused multi-currency abstraction is
/// introduced in the initial domain.
/// </summary>
public readonly record struct DeclaredValue
{
    private DeclaredValue(decimal euros)
    {
        Euros = euros;
    }

    /// <summary>
    /// Gets the exact decimal euro amount used for insurance-threshold
    /// comparisons.
    /// </summary>
    public decimal Euros { get; }

    /// <summary>
    /// Creates a non-negative declared value in euros.
    /// </summary>
    /// <param name="euros">The declared value; zero is valid for legacy rows.</param>
    /// <returns>A validated immutable declared value.</returns>
    /// <exception cref="DomainValidationException">
    /// Thrown when the amount is negative.
    /// </exception>
    public static DeclaredValue FromEuros(decimal euros)
    {
        if (euros < 0m)
        {
            throw new DomainValidationException(
                DomainErrorCodes.DeclaredValueMustBeNonNegative,
                "Declared parcel value cannot be negative.",
                nameof(euros));
        }

        return new DeclaredValue(euros);
    }

    /// <summary>
    /// Formats the amount invariantly for stable decision explanations.
    /// </summary>
    /// <returns>The EUR prefix followed by the exact decimal amount.</returns>
    public override string ToString()
    {
        return $"EUR {Euros.ToString("0.############################", CultureInfo.InvariantCulture)}";
    }
}
