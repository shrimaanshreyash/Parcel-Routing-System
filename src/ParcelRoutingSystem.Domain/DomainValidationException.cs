namespace ParcelRoutingSystem.Domain;

/// <summary>
/// Represents invalid business input rejected before routing. The stable
/// <see cref="Code"/> is safe for outer-layer mapping while the message remains
/// suitable for developers and controlled operator feedback.
/// </summary>
public sealed class DomainValidationException : ArgumentException
{
    /// <summary>
    /// Creates a domain validation failure with a stable code and optional
    /// parameter name.
    /// </summary>
    /// <param name="code">The machine-readable failure code.</param>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="parameterName">The rejected input parameter, when known.</param>
    public DomainValidationException(
        string code,
        string message,
        string? parameterName = null)
        : base(message, parameterName)
    {
        Code = code;
        OperatorMessage = message;
    }

    /// <summary>
    /// Gets the stable failure code used by application and API layers without
    /// coupling them to exception-message wording.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the intentionally authored operator-safe explanation without the
    /// framework-appended parameter suffix used by <see cref="ArgumentException"/>.
    /// </summary>
    public string OperatorMessage { get; }
}
