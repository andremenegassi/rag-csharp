using IA.API.Application.Abstractions;
using IA.API.Domain.Entities;
using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IA.API.Application.Questions;

public sealed class SearchQuestionChunksUseCase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IRagRepository _repository;
    private readonly RagOptions _ragOptions;

    public SearchQuestionChunksUseCase(
        IEmbeddingService embeddingService,
        IRagRepository repository,
        IOptions<RagOptions> ragOptions)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _ragOptions = ragOptions.Value;
    }

    public async Task<IReadOnlyCollection<RetrievedChunk>> ExecuteAsync(SearchQuestionChunksRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new InvalidOperationException("A pergunta é obrigatória.");
        }

        var normalizedTheme = request.Theme?.Trim() ?? string.Empty;
        var topK = NormalizeTopK(request.TopK ?? 5);

        IReadOnlyCollection<float> questionEmbedding;
        try
        {
            questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Question, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Provedor de IA indisponível para gerar embedding da pergunta.", ex);
        }

        try
        {
            return await _repository.SearchChunksAsync(
                normalizedTheme,
                questionEmbedding,
                topK,
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Elasticsearch indisponível para recuperar chunks.", ex);
        }
    }

    private int NormalizeTopK(int topK)
    {
        var effectiveTopK = topK <= 0 ? _ragOptions.DefaultTopK : topK;
        if (effectiveTopK < _ragOptions.MinTopK)
        {
            return _ragOptions.MinTopK;
        }

        if (effectiveTopK > _ragOptions.MaxTopK)
        {
            return _ragOptions.MaxTopK;
        }

        return effectiveTopK;
    }
}
