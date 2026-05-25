namespace IA.API.Application.Documents;

public interface IDocumentUploadQueue
{
    Task<bool> EnqueueAsync(QueuedDocumentItem item, CancellationToken cancellationToken);
    Task<QueuedDocumentItem?> TryDequeueAsync(CancellationToken cancellationToken);
    Task MarkProcessingAsync(Guid queueId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(Guid queueId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid queueId, string error, bool requeue, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QueuedDocumentInfo>> GetPendingAsync(CancellationToken cancellationToken);
}