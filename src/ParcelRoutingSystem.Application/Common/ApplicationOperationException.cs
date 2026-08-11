namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Represents an expected application-use-case failure with a stable code that
/// an outer adapter may map to a safe transport response.
/// </summary>
public sealed class ApplicationOperationException : InvalidOperationException
{
    /// <summary>
    /// Creates a coded application failure without exposing infrastructure or
    /// personal-data details.
    /// </summary>
    /// <param name="code">The stable machine-readable failure code.</param>
    /// <param name="message">The safe human-readable explanation.</param>
    public ApplicationOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the stable code used by future API and worker adapters.
    /// </summary>
    public string Code { get; }
}
