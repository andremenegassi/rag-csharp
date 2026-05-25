namespace IA.API.Application.Abstractions;

public sealed record ChunkInput(string SectionPath, string Content, string? SourceUrl, string? SourceText);

public sealed record ChunkResult(
    string ChunkId,
    int ChunkOrder,
    string SectionPath,
    string Content,
    string? SourceUrl,
    string? SourceText,
    string? Title = null,
    string? Subtitle = null);

public interface IChunkingService
{
    IReadOnlyCollection<ChunkResult> Chunk(IReadOnlyCollection<ChunkInput> sections, int chunkSize, int chunkOverlap);
}
