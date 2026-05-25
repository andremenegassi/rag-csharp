using System.Text;
using IA.API.Application.Abstractions;
using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IA.API.Infrastructure.Rag;

public sealed class ChunkingService : IChunkingService
{
    private readonly RagTokenizer _tokenizer;
    private readonly int _maxChunkUtf8Bytes;

    public ChunkingService(RagTokenizer tokenizer, IOptions<RagOptions> ragOptions)
    {
        _tokenizer = tokenizer;
        _maxChunkUtf8Bytes = Math.Max(1024, ragOptions.Value.MaxChunkUtf8Bytes);
    }

    public IReadOnlyCollection<ChunkResult> Chunk(IReadOnlyCollection<ChunkInput> sections, int chunkSize, int chunkOverlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "ChunkSize deve ser maior que zero.");
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap), "ChunkOverlap deve ser >= 0 e menor que ChunkSize.");
        }

        var chunks = new List<ChunkResult>();
        var chunkOrder = 0;

        foreach (var section in sections)
        {
            var tokens = _tokenizer.Encode(section.Content);
            if (tokens.Count == 0)
            {
                continue;
            }

            var start = 0;
            var step = chunkSize - chunkOverlap;

            while (start < tokens.Count)
            {
                var take = Math.Min(chunkSize, tokens.Count - start);
                var window = tokens.GetRange(start, take);
                chunkOrder = AddTokenWindowChunks(chunks, section, window, chunkOrder);

                if (start + take >= tokens.Count)
                {
                    break;
                }

                start += step;
            }
        }

        return chunks;
    }

    private int AddTokenWindowChunks(
        List<ChunkResult> chunks,
        ChunkInput section,
        List<int> tokens,
        int chunkOrder)
    {
        var content = _tokenizer.Decode(tokens).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return chunkOrder;
        }

        if (Encoding.UTF8.GetByteCount(content) <= _maxChunkUtf8Bytes || tokens.Count == 1)
        {
            chunks.Add(new ChunkResult(
                ChunkId: $"chk_{Guid.NewGuid():N}",
                ChunkOrder: chunkOrder++,
                SectionPath: section.SectionPath,
                Content: content,
                SourceUrl: section.SourceUrl,
                SourceText: section.SourceText));

            return chunkOrder;
        }

        var half = Math.Max(1, tokens.Count / 2);
        chunkOrder = AddTokenWindowChunks(chunks, section, tokens.GetRange(0, half), chunkOrder);
        return AddTokenWindowChunks(chunks, section, tokens.GetRange(half, tokens.Count - half), chunkOrder);
    }

}
