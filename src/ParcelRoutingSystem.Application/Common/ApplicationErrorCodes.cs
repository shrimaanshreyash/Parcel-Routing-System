namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Defines stable application-layer failure codes so future HTTP and worker
/// adapters can translate outcomes without parsing human-readable messages.
/// </summary>
public static class ApplicationErrorCodes
{
    /// <summary>Identifies missing or malformed orchestration metadata.</summary>
    public const string OperationMetadataInvalid = "application.operation_metadata.invalid";

    /// <summary>Identifies a missing or malformed idempotency key.</summary>
    public const string IdempotencyKeyInvalid = "application.idempotency_key.invalid";

    /// <summary>Identifies reuse of a key with different business input.</summary>
    public const string IdempotencyConflict = "application.idempotency_key.conflict";

    /// <summary>Identifies the absence of a safe active routing rule set.</summary>
    public const string ActiveRuleSetUnavailable = "routing.rule_set.active_unavailable";

    /// <summary>Identifies a requested routing decision that does not exist.</summary>
    public const string DecisionNotFound = "routing.decision.not_found";

    /// <summary>Identifies an approval request for a parcel that needs no approval.</summary>
    public const string InsuranceApprovalNotRequired = "routing.approval.not_required";

    /// <summary>Identifies a requested rule-set version that does not exist.</summary>
    public const string RuleSetNotFound = "routing.rule_set.not_found";

    /// <summary>Identifies an activation attempt for a non-draft rule set.</summary>
    public const string RuleSetNotDraft = "routing.rule_set.not_draft";

    /// <summary>Identifies an empty or otherwise invalid batch request.</summary>
    public const string BatchInvalid = "routing.batch.invalid";

    /// <summary>Identifies a requested durable batch that does not exist.</summary>
    public const string BatchNotFound = "routing.batch.not_found";

    /// <summary>Identifies a previously imported normalized manifest.</summary>
    public const string DuplicateManifest = "routing.batch.duplicate_manifest";

    /// <summary>Identifies malformed or unsupported legacy manifest XML.</summary>
    public const string ManifestInvalid = "routing.manifest.invalid";

    /// <summary>Identifies one malformed parcel row isolated inside a valid XML document.</summary>
    public const string ManifestRowInvalid = "routing.manifest.row_invalid";

    /// <summary>Identifies a manifest that exceeds a configured safety limit.</summary>
    public const string ManifestLimitExceeded = "routing.manifest.limit_exceeded";
}
