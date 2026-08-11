namespace ParcelRoutingSystem.Application.Common;

/// <summary>
/// Centralizes small application-boundary checks so every use case applies the
/// same length and presence constraints before reaching persistence.
/// </summary>
internal static class ApplicationGuard
{
    /// <summary>
    /// Returns trimmed required text or fails with the supplied stable error
    /// code before an unsafe identifier reaches logs or database constraints.
    /// </summary>
    /// <param name="value">The untrusted caller-supplied text.</param>
    /// <param name="maximumLength">The maximum persisted character count.</param>
    /// <param name="code">The stable failure code for this field.</param>
    /// <param name="fieldName">The plain-language field name for the message.</param>
    /// <returns>The trimmed validated value.</returns>
    /// <exception cref="ApplicationOperationException">
    /// Thrown when the value is blank or exceeds the configured length.
    /// </exception>
    internal static string RequiredText(
        string? value,
        int maximumLength,
        string code,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApplicationOperationException(code, $"{fieldName} is required.");
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ApplicationOperationException(
                code,
                $"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
