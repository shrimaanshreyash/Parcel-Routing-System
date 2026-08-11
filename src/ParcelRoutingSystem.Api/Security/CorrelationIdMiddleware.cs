using System.Text.RegularExpressions;

namespace ParcelRoutingSystem.Api.Security;

/// <summary>
/// Normalizes one bounded correlation identifier for each request and response
/// without reflecting arbitrary untrusted header text.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    /// <summary>Gets the public correlation header name.</summary>
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates the correlation boundary around the next request delegate.
    /// </summary>
    /// <param name="next">The remaining ASP.NET Core pipeline.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Accepts only a safe bounded caller identifier or generates a server value,
    /// then makes it available to tracing, audit metadata, and the response.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The asynchronous pipeline operation.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        string candidate = context.Request.Headers[HeaderName].ToString();
        string correlationId = CorrelationIdPattern().IsMatch(candidate)
            ? candidate
            : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        return _next(context);
    }

    /// <summary>
    /// Matches only transport-safe correlation characters with a strict
    /// one-to-one-hundred length bound.
    /// </summary>
    /// <returns>The compiled correlation identifier pattern.</returns>
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
