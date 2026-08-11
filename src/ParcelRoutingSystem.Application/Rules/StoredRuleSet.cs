namespace ParcelRoutingSystem.Application.Rules;

/// <summary>
/// Identifies the durable lifecycle state of one immutable rule-set version.
/// </summary>
public enum RuleSetLifecycleStatus
{
    /// <summary>The version is validated but cannot route live parcels.</summary>
    Draft = 1,

    /// <summary>The version is the single policy used for new decisions.</summary>
    Active = 2,

    /// <summary>The version remains historical and can be considered for rollback.</summary>
    Retired = 3,
}

/// <summary>
/// Captures one immutable rule-set definition with its durable lifecycle
/// metadata.
/// </summary>
public sealed record StoredRuleSet(
    RuleSetDefinition Definition,
    RuleSetLifecycleStatus Status,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    DateTimeOffset? ActivatedAtUtc);

/// <summary>
/// Reports whether an idempotent rule-set write created new state or replayed
/// the outcome of an earlier request.
/// </summary>
public sealed record RuleSetWriteResult(StoredRuleSet RuleSet, bool WasCreated);

/// <summary>
/// Reports the active version after an atomic activation or rollback request.
/// </summary>
public sealed record RuleSetActivationResult(StoredRuleSet ActiveRuleSet, bool WasChanged);
