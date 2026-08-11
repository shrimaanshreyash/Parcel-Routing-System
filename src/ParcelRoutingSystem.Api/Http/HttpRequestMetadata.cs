using System.Security.Claims;
using ParcelRoutingSystem.Application.Common;

namespace ParcelRoutingSystem.Api.Http;

/// <summary>
/// Converts authenticated HTTP context and bounded headers into application
/// metadata without passing framework objects into inner layers.
/// </summary>
public static class HttpRequestMetadata
{
    /// <summary>
    /// Creates operation metadata from the authenticated subject and normalized
    /// request trace identifier, failing safely when identity is incomplete.
    /// </summary>
    /// <param name="context">The authenticated current HTTP context.</param>
    /// <returns>Validated transport-independent operation metadata.</returns>
    public static OperationMetadata Create(HttpContext context)
    {
        string? actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        return OperationMetadata.Create(actorId, context.TraceIdentifier);
    }

    /// <summary>
    /// Reads the required idempotency header as plain text so the application
    /// layer applies its single canonical length and missing-value validation.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <returns>The raw header value or an empty value for application validation.</returns>
    public static string GetIdempotencyKey(HttpContext context)
    {
        return context.Request.Headers["Idempotency-Key"].ToString();
    }
}
