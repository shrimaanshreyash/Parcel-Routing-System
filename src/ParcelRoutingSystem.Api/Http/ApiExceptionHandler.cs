using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Imports;
using ParcelRoutingSystem.Domain;
using ParcelRoutingSystem.Domain.Routing;

namespace ParcelRoutingSystem.Api.Http;

/// <summary>
/// Translates expected domain, application, manifest, and infrastructure
/// failures into stable Problem Details without exposing stack traces or input.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    /// <summary>
    /// Creates the centralized safe failure translator.
    /// </summary>
    /// <param name="problemDetails">The framework Problem Details writer.</param>
    /// <param name="logger">The structured server logger.</param>
    public ApiExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<ApiExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    /// <summary>
    /// Maps known safe failures to their HTTP semantics and converts unexpected
    /// exceptions into a correlation-only HTTP 500 response.
    /// </summary>
    /// <param name="httpContext">The failing request context.</param>
    /// <param name="exception">The exception raised by a downstream layer.</param>
    /// <param name="cancellationToken">Cancels response writing.</param>
    /// <returns>True after this handler writes the response.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string code, string title, string detail) =
            Classify(exception);
        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled API failure for correlation {CorrelationId}",
                httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(
                "Rejected API operation {Code} for correlation {CorrelationId}",
                code,
                httpContext.TraceIdentifier);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"urn:parcel-routing-system:problem:{code}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        if (exception is DuplicateManifestException duplicate)
        {
            problem.Extensions["previousBatchId"] = duplicate.PreviousBatchId;
            problem.Extensions["previousImportedAtUtc"] =
                duplicate.PreviousImportedAtUtc;
        }
        httpContext.Response.StatusCode = status;

        return await _problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            });
    }

    /// <summary>
    /// Assigns stable status, code, title, and safe detail values without
    /// inspecting untrusted message text from unknown exception types.
    /// </summary>
    /// <param name="exception">The caught pipeline exception.</param>
    /// <returns>The complete safe HTTP problem classification.</returns>
    private static (int Status, string Code, string Title, string Detail) Classify(
        Exception exception)
    {
        return exception switch
        {
            DuplicateManifestException duplicate => (
                StatusCodes.Status409Conflict,
                ApplicationErrorCodes.DuplicateManifest,
                "Manifest imported previously",
                duplicate.Message),
            ManifestImportException manifest => manifest.Code
                == ApplicationErrorCodes.ManifestLimitExceeded
                ? (
                    StatusCodes.Status413PayloadTooLarge,
                    manifest.Code,
                    "Manifest limit exceeded",
                    manifest.Message)
                : (
                    StatusCodes.Status400BadRequest,
                    manifest.Code,
                    "Manifest rejected",
                    manifest.Message),
            ApplicationOperationException application =>
                ClassifyApplication(application),
            DomainValidationException domain => (
                StatusCodes.Status400BadRequest,
                domain.Code,
                "Parcel validation failed",
                domain.Message),
            RuleSetValidationException rules => (
                StatusCodes.Status400BadRequest,
                "routing.rule_set.invalid",
                "Rule set validation failed",
                rules.Message),
            BadHttpRequestException request when request.StatusCode
                == StatusCodes.Status413PayloadTooLarge => (
                    StatusCodes.Status413PayloadTooLarge,
                    ApplicationErrorCodes.ManifestLimitExceeded,
                    "Request limit exceeded",
                    "The request body exceeds the configured upload limit."),
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                "http.request.invalid",
                "Request rejected",
                "The request could not be read."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "server.unexpected",
                "Unexpected server failure",
                "The operation failed. Use the correlation identifier when contacting support."),
        };
    }

    /// <summary>
    /// Maps stable application codes to not-found, conflict, dependency, or
    /// validation HTTP semantics.
    /// </summary>
    /// <param name="exception">The expected application failure.</param>
    /// <returns>The safe HTTP classification.</returns>
    private static (int Status, string Code, string Title, string Detail)
        ClassifyApplication(ApplicationOperationException exception)
    {
        int status = exception.Code switch
        {
            ApplicationErrorCodes.DecisionNotFound
                or ApplicationErrorCodes.BatchNotFound
                or ApplicationErrorCodes.RuleSetNotFound =>
                StatusCodes.Status404NotFound,
            ApplicationErrorCodes.IdempotencyConflict
                or ApplicationErrorCodes.InsuranceApprovalNotRequired
                or ApplicationErrorCodes.RuleSetNotDraft =>
                StatusCodes.Status409Conflict,
            ApplicationErrorCodes.ActiveRuleSetUnavailable =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };

        return (
            status,
            exception.Code,
            "Operation rejected",
            exception.Message);
    }
}
