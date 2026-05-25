namespace IA.API.Application.Abstractions;

public sealed record ParsedSection(string SectionPath, string Content, string? SourceUrl, string? SourceText);

public sealed record ParsedDocument(
    string FileName,
    string ContentType,
    IReadOnlyCollection<ParsedSection> Sections);

public interface IDocumentParser
{
    bool CanHandle(string contentType, string fileName);

    Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken);
}
