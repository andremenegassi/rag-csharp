using System.Text;
using System.Text.RegularExpressions;
using IA.API.Application.Abstractions;

namespace IA.API.Infrastructure.Parsers;

public sealed class MarkdownDocumentParser : IDocumentParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex SourceRegex = new(@"^Fonte:\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanHandle(string contentType, string fileName)
    {
        return contentType.Contains("markdown", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var markdown = await reader.ReadToEndAsync(cancellationToken);

        var sections = ParseSections(markdown);
        if (sections.Count == 0)
        {
            sections.Add(new ParsedSection("Root", markdown.Trim(), null, null));
        }

        return new ParsedDocument(fileName, contentType, sections);
    }

    private static List<ParsedSection> ParseSections(string markdown)
    {
        var result = new List<ParsedSection>();
        var sourceByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceTextByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var stack = new List<(int Level, string Title)>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        string currentPath = "Root";
        string? currentSourceUrl = null;
        string? currentSourceText = null;
        var currentBuffer = new StringBuilder();

        void FlushCurrent()
        {
            var content = currentBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(content) && currentPath == "Root")
            {
                currentBuffer.Clear();
                return;
            }

            var inheritedSource = currentSourceUrl ?? ResolveInheritedSource(currentPath, sourceByPath);
            var inheritedSourceText = currentSourceText ?? ResolveInheritedSource(currentPath, sourceTextByPath);
            result.Add(new ParsedSection(currentPath, content, inheritedSource, inheritedSourceText));
            currentBuffer.Clear();
            currentSourceUrl = null;
            currentSourceText = null;
        }

        foreach (var line in lines)
        {
            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                FlushCurrent();

                var level = headingMatch.Groups[1].Value.Length;
                var title = headingMatch.Groups[2].Value.Trim();

                while (stack.Count > 0 && stack[^1].Level >= level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                stack.Add((level, title));
                currentPath = string.Join(" > ", stack.Select(s => s.Title));
                continue;
            }

            var sourceMatch = SourceRegex.Match(line.Trim());
            if (sourceMatch.Success)
            {
                var sourceValue = sourceMatch.Groups[1].Value.Trim();
                currentSourceText = sourceValue;

                if (Uri.TryCreate(sourceValue, UriKind.Absolute, out _))
                {
                    currentSourceUrl = sourceValue;
                }

                sourceByPath[currentPath] = currentSourceUrl ?? string.Empty;
                sourceTextByPath[currentPath] = currentSourceText;
                continue;
            }

            currentBuffer.AppendLine(line);
        }

        FlushCurrent();

        for (var i = 0; i < result.Count; i++)
        {
            var section = result[i];
            if (!string.IsNullOrWhiteSpace(section.SourceUrl) || !string.IsNullOrWhiteSpace(section.SourceText))
            {
                continue;
            }

            var inheritedUrl = ResolveInheritedSource(section.SectionPath, sourceByPath);
            var inheritedText = ResolveInheritedSource(section.SectionPath, sourceTextByPath);
            result[i] = section with { SourceUrl = inheritedUrl, SourceText = inheritedText };
        }

        return result;
    }

    private static string? ResolveInheritedSource(string sectionPath, IReadOnlyDictionary<string, string> sourceByPath)
    {
        if (sourceByPath.TryGetValue(sectionPath, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var parts = sectionPath.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
        for (var size = parts.Length - 1; size > 0; size--)
        {
            var parentPath = string.Join(" > ", parts.Take(size));
            if (sourceByPath.TryGetValue(parentPath, out var inherited) && !string.IsNullOrWhiteSpace(inherited))
            {
                return inherited;
            }
        }

        return null;
    }
}
