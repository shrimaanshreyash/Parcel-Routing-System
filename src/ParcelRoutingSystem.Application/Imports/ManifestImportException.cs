namespace ParcelRoutingSystem.Application.Imports;

/// <summary>
/// Represents an expected safe XML import failure whose stable code can be
/// translated by HTTP without exposing raw uploaded content or parser details.
/// </summary>
public sealed class ManifestImportException : Exception
{
    /// <summary>
    /// Creates a privacy-safe manifest failure for an unsupported shape or
    /// exceeded parser limit.
    /// </summary>
    /// <param name="code">The stable application error category.</param>
    /// <param name="message">The safe operator-facing explanation.</param>
    public ManifestImportException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Gets the stable transport-independent failure category.</summary>
    public string Code { get; }
}
