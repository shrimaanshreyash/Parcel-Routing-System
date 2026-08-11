using System.Collections.ObjectModel;

namespace ParcelRoutingSystem.Domain.Routing;

/// <summary>
/// Represents a proposed rule set that is unsafe to evaluate because one or
/// more semantic invariants failed.
/// </summary>
public sealed class RuleSetValidationException : InvalidOperationException
{
    /// <summary>
    /// Creates one failure containing every detected semantic problem so a rule
    /// author can correct the complete draft before retrying.
    /// </summary>
    /// <param name="errors">The ordered human-readable validation findings.</param>
    internal RuleSetValidationException(IEnumerable<string> errors)
        : this(errors.ToArray())
    {
    }

    /// <summary>
    /// Creates the immutable exception after materializing the error sequence
    /// once, preventing deferred enumeration from changing diagnostics.
    /// </summary>
    /// <param name="errors">The materialized semantic validation findings.</param>
    private RuleSetValidationException(string[] errors)
        : base($"Routing rule set is unsafe: {string.Join(" ", errors)}")
    {
        Errors = new ReadOnlyCollection<string>(errors);
    }

    /// <summary>
    /// Gets all semantic findings in deterministic validation order.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
