namespace ParcelRoutingSystem.Api.Security;

/// <summary>
/// Centralizes stable role and policy names so controllers, authentication, and
/// tests enforce the same least-privilege vocabulary.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Identifies the parcel-entry and import role.</summary>
    public const string OperatorRole = "Operator";

    /// <summary>Identifies the insurance approval role.</summary>
    public const string InsuranceApproverRole = "InsuranceApprover";

    /// <summary>Identifies the constrained rule administration role.</summary>
    public const string RuleAdministratorRole = "RuleAdministrator";

    /// <summary>Requires any authenticated reviewer identity.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Requires the Operator role.</summary>
    public const string Operator = "OperatorPolicy";

    /// <summary>Requires the Insurance Approver role.</summary>
    public const string InsuranceApprover = "InsuranceApproverPolicy";

    /// <summary>Requires the Rule Administrator role.</summary>
    public const string RuleAdministrator = "RuleAdministratorPolicy";
}

/// <summary>
/// Names endpoint-specific rate limits by business cost rather than controller
/// implementation.
/// </summary>
public static class ApiRateLimitPolicies
{
    /// <summary>Limits ordinary routing writes.</summary>
    public const string Routing = "routing";

    /// <summary>Limits higher-cost streamed XML imports.</summary>
    public const string Upload = "upload";

    /// <summary>Limits insurance approval writes.</summary>
    public const string Approval = "approval";

    /// <summary>Limits bounded read-model queries.</summary>
    public const string Query = "query";
}
