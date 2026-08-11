namespace ParcelRoutingSystem.Domain;

/// <summary>
/// Defines stable machine-readable codes for domain validation failures so
/// outer layers can translate errors without parsing human-readable messages.
/// </summary>
public static class DomainErrorCodes
{
    /// <summary>Identifies a zero or negative parcel weight.</summary>
    public const string WeightMustBePositive = "parcel.weight.must_be_positive";

    /// <summary>Identifies a declared value below zero euros.</summary>
    public const string DeclaredValueMustBeNonNegative =
        "parcel.declared_value.must_be_non_negative";

    /// <summary>Identifies a missing destination country.</summary>
    public const string CountryRequired = "parcel.country.required";

    /// <summary>Identifies an unassigned ISO alpha-2 country code.</summary>
    public const string CountryInvalid = "parcel.country.invalid";

    /// <summary>Identifies a blank optional-attribute name.</summary>
    public const string AdditionalAttributeNameInvalid =
        "parcel.additional_attribute.name_invalid";

    /// <summary>Identifies a missing optional-attribute value.</summary>
    public const string AdditionalAttributeValueInvalid =
        "parcel.additional_attribute.value_invalid";

    /// <summary>Identifies a malformed stable rule identifier.</summary>
    public const string RuleIdInvalid = "routing.rule.id_invalid";

    /// <summary>Identifies a non-positive immutable rule-set version.</summary>
    public const string RuleSetVersionInvalid = "routing.rule_set.version_invalid";

    /// <summary>Identifies an invalid constrained rule field.</summary>
    public const string RoutingRuleInvalid = "routing.rule.invalid";

    /// <summary>Identifies an undefined routing department.</summary>
    public const string RoutingDepartmentInvalid = "routing.department.invalid";

    /// <summary>Identifies missing or invalid caller-owned decision metadata.</summary>
    public const string DecisionContextInvalid = "routing.decision_context.invalid";
}
