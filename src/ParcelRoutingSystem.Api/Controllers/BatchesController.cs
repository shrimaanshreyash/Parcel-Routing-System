using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ParcelRoutingSystem.Api.Configuration;
using ParcelRoutingSystem.Api.Contracts;
using ParcelRoutingSystem.Api.Http;
using ParcelRoutingSystem.Api.Security;
using ParcelRoutingSystem.Application.Batches;
using ParcelRoutingSystem.Application.Common;
using ParcelRoutingSystem.Application.Imports;
using ParcelRoutingSystem.Application.Operations;

namespace ParcelRoutingSystem.Api.Controllers;

/// <summary>
/// Exposes bounded raw-stream XML import and durable progress polling to the
/// server-enforced Operator capability.
/// </summary>
[ApiController]
[Route("api/batches")]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public sealed class BatchesController : ControllerBase
{
    private static readonly HashSet<string> AcceptedMediaTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/xml",
            "text/xml",
        };

    private readonly IParcelManifestParser _parser;
    private readonly CreateBatchUseCase _createBatch;
    private readonly OperationsQueryUseCase _operations;
    private readonly ParcelManifestOptions _options;

    /// <summary>
    /// Creates the import controller with secure parsing, durable batch
    /// orchestration, bounded reads, and validated upload limits.
    /// </summary>
    /// <param name="parser">The hardened streaming XML parser.</param>
    /// <param name="createBatch">The transactional batch creation use case.</param>
    /// <param name="operations">The bounded batch read use case.</param>
    /// <param name="options">The validated HTTP and parser limits.</param>
    public BatchesController(
        IParcelManifestParser parser,
        CreateBatchUseCase createBatch,
        OperationsQueryUseCase operations,
        IOptions<ParcelManifestOptions> options)
    {
        _parser = parser;
        _createBatch = createBatch;
        _operations = operations;
        _options = options.Value;
    }

    /// <summary>
    /// Validates XML transport metadata, streams the untrusted body into the
    /// hardened parser, persists the batch, and returns an asynchronous resource.
    /// </summary>
    /// <param name="fallbackCountry">Explicit country for rows that omit one.</param>
    /// <param name="confirmDuplicate">True only after the operator confirms a prior match.</param>
    /// <param name="cancellationToken">Cancels parsing and persistence.</param>
    /// <returns>The newly accepted or replayed durable batch.</returns>
    [HttpPost("import-xml")]
    [EnableRateLimiting(ApiRateLimitPolicies.Upload)]
    [RequestSizeLimit(2_097_152)]
    [ProducesResponseType<BatchResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<BatchResponse>> ImportXmlAsync(
        [FromQuery] string? fallbackCountry,
        [FromQuery] bool confirmDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        ValidateXmlTransport(Request, _options.MaximumBytes);
        ParsedParcelManifest manifest = await _parser.ParseAsync(
            Request.Body,
            cancellationToken);
        var command = new CreateBatchCommand(
            HttpRequestMetadata.GetIdempotencyKey(HttpContext),
            fallbackCountry,
            manifest.Rows,
            HttpRequestMetadata.Create(HttpContext),
            AllowDuplicate: confirmDuplicate);
        BatchWriteResult writeResult = await _createBatch.ExecuteAsync(
            command,
            cancellationToken);
        BatchDetails details = await _operations.GetBatchAsync(
            writeResult.Batch.Id,
            cancellationToken);
        BatchResponse response = ApiContractMapper.ToResponse(
            details,
            writeResult.WasCreated);

        return Accepted(
            $"/api/batches/{response.Id:D}",
            response);
    }

    /// <summary>
    /// Returns newest durable imports under a strict bound so import history
    /// survives navigation and browser refresh.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<IReadOnlyList<BatchSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BatchSummaryResponse>>>
        GetRecentAsync(
            [FromQuery] int limit = 20,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BatchSummary> batches =
            await _operations.GetRecentBatchesAsync(limit, cancellationToken);
        return Ok(batches.Select(ApiContractMapper.ToResponse).ToArray());
    }

    /// <summary>
    /// Returns durable batch progress and any completed row decisions so the
    /// browser can poll without keeping the upload request open.
    /// </summary>
    /// <param name="batchId">The server-owned non-sequential batch identifier.</param>
    /// <param name="cancellationToken">Cancels persistence reads.</param>
    /// <returns>The current batch state and ordered rows.</returns>
    [HttpGet("{batchId:guid}")]
    [EnableRateLimiting(ApiRateLimitPolicies.Query)]
    [ProducesResponseType<BatchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchResponse>> GetBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        BatchDetails details = await _operations.GetBatchAsync(
            batchId,
            cancellationToken);
        return Ok(ApiContractMapper.ToResponse(details));
    }

    /// <summary>
    /// Enforces supported media type, XML filename metadata, non-empty body, and
    /// declared length before any parser or persistence work begins.
    /// </summary>
    /// <param name="request">The untrusted HTTP request.</param>
    /// <param name="maximumBytes">The configured request byte limit.</param>
    private static void ValidateXmlTransport(
        HttpRequest request,
        long maximumBytes)
    {
        string mediaType = request.ContentType?
            .Split(';', 2, StringSplitOptions.TrimEntries)[0]
            ?? string.Empty;
        if (!AcceptedMediaTypes.Contains(mediaType))
        {
            throw new ManifestImportException(
                ApplicationErrorCodes.ManifestInvalid,
                "Content-Type must be application/xml or text/xml.");
        }

        if (request.ContentLength is 0)
        {
            throw new ManifestImportException(
                ApplicationErrorCodes.ManifestInvalid,
                "The XML request body is empty.");
        }

        if (request.ContentLength > maximumBytes)
        {
            throw new ManifestImportException(
                ApplicationErrorCodes.ManifestLimitExceeded,
                $"The XML request exceeds the {maximumBytes} byte limit.");
        }

        string manifestName = request.Headers["X-Manifest-Name"].ToString();
        if (manifestName.Length is < 1 or > 128
            || !string.Equals(
                Path.GetFileName(manifestName),
                manifestName,
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetExtension(manifestName),
                ".xml",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestImportException(
                ApplicationErrorCodes.ManifestInvalid,
                "A safe XML manifest filename is required.");
        }
    }
}
