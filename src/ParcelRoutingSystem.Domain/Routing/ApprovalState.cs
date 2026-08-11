namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Describes whether routing may continue immediately or must pause for the
/// required high-value insurance workflow.
/// </summary>
public enum ApprovalState
{
    /// <summary>The parcel value does not require insurance approval.</summary>
    NotRequired = 1,

    /// <summary>
    /// The intended department is known, but dispatch must wait for insurance
    /// approval.
    /// </summary>
    PendingInsuranceApproval = 2,
}
