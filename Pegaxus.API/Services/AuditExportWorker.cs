using Pegaxus.Service.AuditService;

namespace Pegaxus.API.Services;

/// <summary>Builds audit packages one at a time, in the order they were asked for.</summary>
public sealed class AuditExportWorker(AuditExportQueue queue, IServiceScopeFactory scopes, ILogger<AuditExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var exportId in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAuditService>().ProcessExportAsync(exportId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "Audit export {ExportId} crashed the worker step; the next job continues.", exportId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
