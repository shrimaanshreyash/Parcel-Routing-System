using Microsoft.Extensions.Options;
using ParcelRoutingSystem.Api.Configuration;
using ParcelRoutingSystem.Application.Batches;

namespace ParcelRoutingSystem.Api.Workers;

/// <summary>
/// Runs the durable database-backed batch queue inside the modular monolith and
/// creates a fresh dependency scope for every independently claimable row.
/// </summary>
public sealed class DurableBatchProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BatchProcessorOptions _options;
    private readonly ILogger<DurableBatchProcessor> _logger;

    /// <summary>
    /// Creates the hosted worker without capturing request-scoped use cases or
    /// EF contexts in its singleton lifetime.
    /// </summary>
    /// <param name="scopeFactory">The factory used for one-row scopes.</param>
    /// <param name="options">Validated polling and failure delays.</param>
    /// <param name="logger">The privacy-safe structured worker logger.</param>
    public DurableBatchProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<BatchProcessorOptions> options,
        ILogger<DurableBatchProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Claims and processes durable rows until shutdown, delaying on empty
    /// queues and unexpected failures while expired leases provide recovery.
    /// </summary>
    /// <param name="stoppingToken">Signals graceful host shutdown.</param>
    /// <returns>The worker lifetime task.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Durable batch processor is disabled by configuration.");
            return;
        }

        _logger.LogInformation("Durable batch processor started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                BatchRowProcessResult result = await ProcessOneAsync(stoppingToken);
                if (result.Status == BatchRowProcessStatus.NoWork)
                {
                    await Task.Delay(
                        _options.IdleDelayMilliseconds,
                        stoppingToken);
                }
                else if (result.Status == BatchRowProcessStatus.Deferred)
                {
                    await Task.Delay(
                        _options.FailureDelayMilliseconds,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Batch processor attempt failed; the row lease will recover safely.");
                await Task.Delay(
                    _options.FailureDelayMilliseconds,
                    stoppingToken);
            }
        }

        _logger.LogInformation("Durable batch processor stopped.");
    }

    /// <summary>
    /// Resolves one scoped application use case and performs one claim attempt so
    /// every EF context is disposed before the next loop iteration.
    /// </summary>
    /// <param name="cancellationToken">Cancels the current claim and processing.</param>
    /// <returns>The observable outcome of one queue attempt.</returns>
    private async Task<BatchRowProcessResult> ProcessOneAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ProcessNextBatchRowUseCase useCase =
            scope.ServiceProvider.GetRequiredService<ProcessNextBatchRowUseCase>();
        string correlationId = $"worker:{Guid.NewGuid():N}";
        BatchRowProcessResult result = await useCase.ExecuteAsync(
            "system:batch-processor",
            correlationId,
            cancellationToken);

        if (result.Status != BatchRowProcessStatus.NoWork)
        {
            _logger.LogInformation(
                "Batch row attempt ended with {Status}, row {RowId}, correlation {CorrelationId}",
                result.Status,
                result.RowId,
                correlationId);
        }

        return result;
    }
}
