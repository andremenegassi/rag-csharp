using IA.API.Domain.Entities;

namespace IA.API.Application.Abstractions;

public interface IRagRepository
{
    Task<bool> ExistsDocumentByHashAsync(string theme, string fileHash, CancellationToken cancellationToken);

    Task<bool> ExistsDocumentByFileNameAsync(string theme, string fileName, CancellationToken cancellationToken);

    Task<bool> DeleteDocumentAsync(string theme, string documentId, CancellationToken cancellationToken);
    
    Task<bool> DeleteDocumentByHashAsync(string theme, string fileHash, CancellationToken cancellationToken);
    Task<bool> DeleteDocumentByFileNameAsync(string theme, string fileName, CancellationToken cancellationToken);

    Task IndexDocumentAsync(DocumentMetadata document, CancellationToken cancellationToken);

    Task IndexChunksAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<DocumentMetadata>> ListDocumentsAsync(string? theme, CancellationToken cancellationToken);

    Task<DocumentMetadata?> GetDocumentByIdAsync(string documentId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> ListThemesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RetrievedChunk>> SearchChunksAsync(
        string theme,
        IReadOnlyCollection<float> questionEmbedding,
        int topK,
        CancellationToken cancellationToken);
}
