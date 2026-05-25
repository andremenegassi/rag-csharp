using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IA.API.Application.Abstractions;
using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IA.API.Infrastructure.OpenAI;

public sealed class OpenAiSemanticClient : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiOptions _openAiOptions;

    public OpenAiSemanticClient(IHttpClientFactory httpClientFactory, IOptions<OpenAiOptions> openAiOptions)
    {
        _httpClientFactory = httpClientFactory;
        _openAiOptions = openAiOptions.Value;
    }

    public async Task<IReadOnlyCollection<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var embeddings = await GenerateEmbeddingsAsync([text], cancellationToken);
        return embeddings.Count == 0 ? Array.Empty<float>() : embeddings[0];
    }

    public async Task<IReadOnlyList<IReadOnlyCollection<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var normalizedInputs = texts
            .Select(static t => t?.Trim() ?? string.Empty)
            .ToArray();

        var indexedInputs = normalizedInputs
            .Select((text, index) => new { Index = index, Text = text })
            .Where(static x => !string.IsNullOrWhiteSpace(x.Text))
            .ToArray();

        if (indexedInputs.Length == 0)
        {
            return Enumerable
                .Range(0, texts.Count)
                .Select(_ => (IReadOnlyCollection<float>)Array.Empty<float>())
                .ToArray();
        }

        var apiKey = !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey)
            ? _openAiOptions.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY não configurada para gerar embeddings.");
        }

        var endpoint = (_openAiOptions.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/') + "/embeddings";
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_openAiOptions.EmbeddingModel)
                ? "text-embedding-3-large"
                : _openAiOptions.EmbeddingModel,
            input = indexedInputs.Select(static x => x.Text).ToArray()
        };

        var client = _httpClientFactory.CreateClient(nameof(OpenAiSemanticClient));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao gerar embedding na OpenAI. Status={(int)response.StatusCode} Body={responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            return Enumerable
                .Range(0, texts.Count)
                .Select(_ => (IReadOnlyCollection<float>)Array.Empty<float>())
                .ToArray();
        }

        var vectorsByIndex = data
            .EnumerateArray()
            .Select(item => new
            {
                Index = item.GetProperty("index").GetInt32(),
                Vector = (IReadOnlyCollection<float>)item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()
            })
            .ToDictionary(x => x.Index, x => x.Vector);

        var result = Enumerable
            .Range(0, texts.Count)
            .Select(_ => (IReadOnlyCollection<float>)Array.Empty<float>())
            .ToArray();

        for (var i = 0; i < indexedInputs.Length; i++)
        {
            var originalIndex = indexedInputs[i].Index;
            result[originalIndex] = vectorsByIndex.TryGetValue(i, out var vector)
                ? vector
                : Array.Empty<float>();
        }

        return result;
    }
}
