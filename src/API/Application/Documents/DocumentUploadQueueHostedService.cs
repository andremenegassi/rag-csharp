using Microsoft.Extensions.Hosting;

namespace IA.API.Application.Documents;

public sealed class DocumentUploadQueueHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDocumentUploadQueue _queue;
    private readonly ILogger<DocumentUploadQueueHostedService> _logger;

    public DocumentUploadQueueHostedService(
        IServiceScopeFactory scopeFactory,
        IDocumentUploadQueue queue,
        ILogger<DocumentUploadQueueHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var item = await _queue.TryDequeueAsync(stoppingToken);
            if (item is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            await _queue.MarkProcessingAsync(item.QueueId, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<UploadDocumentUseCase>();

                await using var stream = new MemoryStream(item.Content, writable: false);
                await useCase.ExecuteAsync(
                    new UploadDocumentRequest(stream, item.FileName, item.ContentType, item.Theme, true),
                    stoppingToken);

                await _queue.MarkCompletedAsync(item.QueueId, stoppingToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("duplicado", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Item {QueueId} removido da fila por duplicidade.", item.QueueId);
                await _queue.MarkCompletedAsync(item.QueueId, stoppingToken);
            }
            catch (Exception ex)
            {
                var requeue = item.Attempts < 3;
                _logger.LogError(ex, "Falha ao processar item {QueueId}. Requeue={Requeue}", item.QueueId, requeue);
                await _queue.MarkFailedAsync(item.QueueId, ex.Message, requeue, stoppingToken);
            }
        }
    }
}