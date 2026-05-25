using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace IA.API.Application.Documents;

public sealed class MemoryCacheDocumentUploadQueue : IDocumentUploadQueue
{
    private const string QueueKey = "documents:upload:queue";
    private const string PendingKey = "documents:upload:pending";
    private const string HashIndexKey = "documents:upload:hash-index";

    private readonly IMemoryCache _memoryCache;
    private readonly object _sync = new();

    public MemoryCacheDocumentUploadQueue(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public Task<bool> EnqueueAsync(QueuedDocumentItem item, CancellationToken cancellationToken)
    {
        var queue = GetQueue();
        var pending = GetPending();
        var hashIndex = GetHashIndex();
        var hashKey = ComposeHashKey(item.Theme, item.FileHash);

        lock (_sync)
        {
            if (hashIndex.ContainsKey(hashKey))
            {
                return Task.FromResult(false);
            }

            pending[item.QueueId] = item;
            hashIndex[hashKey] = item.QueueId;
            queue.Enqueue(item.QueueId);
            return Task.FromResult(true);
        }
    }

    public Task<QueuedDocumentItem?> TryDequeueAsync(CancellationToken cancellationToken)
    {
        var queue = GetQueue();
        var pending = GetPending();

        while (queue.TryDequeue(out var queueId))
        {
            if (pending.TryGetValue(queueId, out var item))
            {
                return Task.FromResult<QueuedDocumentItem?>(item);
            }
        }

        return Task.FromResult<QueuedDocumentItem?>(null);
    }

    public Task MarkProcessingAsync(Guid queueId, CancellationToken cancellationToken)
    {
        var pending = GetPending();
        if (pending.TryGetValue(queueId, out var item))
        {
            pending[queueId] = item with { Status = QueuedDocumentStatus.Processing };
        }

        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(Guid queueId, CancellationToken cancellationToken)
    {
        var pending = GetPending();
        var hashIndex = GetHashIndex();

        lock (_sync)
        {
            if (pending.TryRemove(queueId, out var item))
            {
                hashIndex.TryRemove(ComposeHashKey(item.Theme, item.FileHash), out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid queueId, string error, bool requeue, CancellationToken cancellationToken)
    {
        var queue = GetQueue();
        var pending = GetPending();

        if (!pending.TryGetValue(queueId, out var item))
        {
            return Task.CompletedTask;
        }

        var updated = item with
        {
            Attempts = item.Attempts + 1,
            LastError = error,
            Status = requeue ? QueuedDocumentStatus.Pending : QueuedDocumentStatus.Failed
        };

        pending[queueId] = updated;

        if (requeue)
        {
            queue.Enqueue(queueId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<QueuedDocumentInfo>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var pending = GetPending();
        IReadOnlyCollection<QueuedDocumentInfo> items = pending.Values
            .OrderBy(x => x.EnqueuedAt)
            .Select(x => new QueuedDocumentInfo(
                x.QueueId,
                x.Theme,
                x.FileName,
                x.ContentType,
                x.FileHash,
                x.EnqueuedAt,
                x.Attempts,
                x.Status.ToString(),
                x.LastError))
            .ToArray();

        return Task.FromResult(items);
    }

    private ConcurrentQueue<Guid> GetQueue() =>
        _memoryCache.GetOrCreate(QueueKey, _ => new ConcurrentQueue<Guid>())!;

    private ConcurrentDictionary<Guid, QueuedDocumentItem> GetPending() =>
        _memoryCache.GetOrCreate(PendingKey, _ => new ConcurrentDictionary<Guid, QueuedDocumentItem>())!;

    private ConcurrentDictionary<string, Guid> GetHashIndex() =>
        _memoryCache.GetOrCreate(HashIndexKey, _ => new ConcurrentDictionary<string, Guid>(StringComparer.OrdinalIgnoreCase))!;

    private static string ComposeHashKey(string theme, string hash) =>
        $"{(theme ?? string.Empty).Trim().ToLowerInvariant()}::{(hash ?? string.Empty).Trim()}";
}