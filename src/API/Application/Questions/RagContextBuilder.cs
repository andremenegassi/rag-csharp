using IA.API.Domain.Entities;
using IA.API.Infrastructure.Options;
using IA.API.Infrastructure.Rag;
using Microsoft.Extensions.Options;

namespace IA.API.Application.Questions;

public sealed class RagContextBuilder
{
    private readonly RagOptions _options;
    private readonly RagTokenizer _tokenizer;

    public RagContextBuilder(IOptions<RagOptions> options, RagTokenizer tokenizer)
    {
        _options = options.Value;
        _tokenizer = tokenizer;
    }

    public string Build(IReadOnlyCollection<RetrievedChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return "CONTEXT:\nSem contexto relevante recuperado para a pergunta.";
        }

        var tokenLimit = Math.Max(200, _options.ContextTokenLimit);
        var parts = new List<string>();
        var tokenCount = 0;

        foreach (var chunk in chunks)
        {
            var estimated = _tokenizer.CountTokens(chunk.Content);
            if (tokenCount + estimated > tokenLimit)
            {
                break;
            }

            tokenCount += estimated;
            parts.Add($"[chunkId={chunk.ChunkId}] [doc={chunk.DocumentId}] [section={chunk.SectionPath}]\n{chunk.Content}");
        }

        return "CONTEXT:\n" + string.Join("\n\n", parts);
    }
}
