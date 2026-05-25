namespace IA.API.Application.Documents;

public enum QueuedDocumentStatus
{
    Pending = 1,
    Processing = 2,
    Failed = 3
}

public sealed record QueuedDocumentItem(
    Guid QueueId,
    string Theme,
    string FileName,
    string ContentType,
    string FileHash,
    byte[] Content,
    DateTime EnqueuedAt,
    int Attempts,
    string? LastError,
    QueuedDocumentStatus Status)
{
    public static QueuedDocumentItem Create(
        string theme,
        string fileName,
        string contentType,
        string fileHash,
        byte[] content) =>
        new(
            QueueId: Guid.NewGuid(),
            Theme: theme,
            FileName: fileName,
            ContentType: contentType,
            FileHash: fileHash,
            Content: content,
            EnqueuedAt: DateTime.UtcNow,
            Attempts: 0,
            LastError: null,
            Status: QueuedDocumentStatus.Pending);
}

public sealed record QueuedDocumentInfo(
    Guid QueueId,
    string Theme,
    string FileName,
    string ContentType,
    string FileHash,
    DateTime EnqueuedAt,
    int Attempts,
    string Status,
    string? LastError);