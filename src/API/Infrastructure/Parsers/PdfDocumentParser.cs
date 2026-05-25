using System.Text;
using IA.API.Application.Abstractions;
using UglyToad.PdfPig;

namespace IA.API.Infrastructure.Parsers;

public sealed class PdfDocumentParser : IDocumentParser
{
    public bool CanHandle(string contentType, string fileName)
    {
        return contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        fileStream.CopyTo(memory);
        memory.Position = 0;

        var sections = new List<ParsedSection>();
        using (var document = PdfDocument.Open(memory))
        {
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.GetPage(pageNumber);
                var text = NormalizeText(page.Text);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sections.Add(new ParsedSection($"Pagina {pageNumber}", text, null, null));
                }
            }
        }

        if (sections.Count == 0)
        {
            sections.Add(new ParsedSection("Documento", string.Empty, null, null));
        }

        return Task.FromResult(new ParsedDocument(fileName, contentType, sections));
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder();
        var previousWhitespace = false;

        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
                continue;
            }

            previousWhitespace = false;
            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }
}
