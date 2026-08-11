namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Provides the stable identifiers of the default routing rules so
/// tests, audits, and later persistence can refer to the same business effects.
/// </summary>
public static class DefaultRoutingRuleIds
{
    /// <summary>
    /// Gets the identifier for weights up to and including one kilogram.
    /// </summary>
    public static RuleId MailWeight { get; } = RuleId.From("WEIGHT-MAIL-UP-TO-1-KG");

    /// <summary>
    /// Gets the identifier for weights above one and up to ten kilograms.
    /// </summary>
    public static RuleId RegularWeight { get; } =
        RuleId.From("WEIGHT-REGULAR-UP-TO-10-KG");

    /// <summary>
    /// Gets the identifier for weights above ten kilograms.
    /// </summary>
    public static RuleId HeavyWeight { get; } = RuleId.From("WEIGHT-HEAVY-OVER-10-KG");

    /// <summary>
    /// Gets the identifier for declared values above EUR 1,000.
    /// </summary>
    public static RuleId InsuranceValue { get; } =
        RuleId.From("VALUE-INSURANCE-OVER-1000-EUR");
}
