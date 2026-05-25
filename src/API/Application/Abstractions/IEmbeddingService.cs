namespace IA.API.Application.Abstractions;

public interface IEmbeddingService
{
    Task<IReadOnlyCollection<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyCollection<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
