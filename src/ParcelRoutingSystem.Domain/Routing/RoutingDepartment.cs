namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Defines the allow-listed departments supported by the current routing
/// domain. New departments require an explicit reviewed code change.
/// </summary>
public enum RoutingDepartment
{
    /// <summary>Handles parcels weighing up to and including 1 kg.</summary>
    Mail = 1,

    /// <summary>Handles parcels above 1 kg and up to and including 10 kg.</summary>
    Regular = 2,

    /// <summary>Handles parcels weighing more than 10 kg.</summary>
    Heavy = 3,
}
