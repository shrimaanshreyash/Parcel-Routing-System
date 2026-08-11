namespace ParcelRoutingSystem.Api.Security;

/// <summary>
/// Adds conservative browser security headers to API responses as
/// defense-in-depth without changing authentication or authorization behavior.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates the response-header boundary around the remaining pipeline.
    /// </summary>
    /// <param name="next">The remaining request delegate.</param>
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Applies non-sniffing, clickjacking, referrer, permissions, and API-only
    /// content policies before the response starts.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The asynchronous pipeline operation.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

        return _next(context);
    }
}
