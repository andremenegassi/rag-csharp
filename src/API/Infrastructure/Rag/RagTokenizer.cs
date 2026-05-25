using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;
using SharpToken;

namespace IA.API.Infrastructure.Rag;

public sealed class RagTokenizer
{
    private readonly GptEncoding _encoding;

    public RagTokenizer(IOptions<OpenAiOptions> openAiOptions)
    {
        var encodingName = GetEncodingName(openAiOptions.Value.EmbeddingModel);
        _encoding = GptEncoding.GetEncoding(encodingName);
    }

    public List<int> Encode(string? content)
    {
        return _encoding.Encode(NormalizeContent(content));
    }

    public string Decode(IEnumerable<int> tokens)
    {
        return _encoding.Decode(tokens.ToList());
    }

    public int CountTokens(string? content)
    {
        return Encode(content).Count;
    }

    private static string NormalizeContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private static string GetEncodingName(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "cl100k_base";
        }

        var modelName = model.Trim();
        if (modelName.StartsWith("text-embedding-", StringComparison.OrdinalIgnoreCase))
        {
            return "cl100k_base";
        }

        return modelName switch
        {
            "text-embedding-3-large" => "cl100k_base",
            "text-embedding-3-small" => "cl100k_base",
            "text-embedding-ada-002" => "cl100k_base",
            _ => Model.GetEncodingNameForModel(modelName)
        };
    }
}
