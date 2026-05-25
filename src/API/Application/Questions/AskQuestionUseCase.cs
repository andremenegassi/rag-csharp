using IA.API.Application.Abstractions;
using IA.API.Domain.Entities;
using IA.API.Infrastructure.OpenAI;
using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace IA.API.Application.Questions;

public sealed class AskQuestionUseCase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IRagRepository _repository;
    private readonly RagContextBuilder _contextBuilder;
    private readonly RagOptions _ragOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiOptions _openAiOptions;
    private readonly IQuestionSessionMemoryStore _sessionMemoryStore;

    public AskQuestionUseCase(
        IEmbeddingService embeddingService,
        IRagRepository repository,
        RagContextBuilder contextBuilder,
        IOptions<RagOptions> ragOptions,
        IHttpClientFactory httpClientFactory,
        IOptions<OpenAiOptions> openAiOptions,
        IQuestionSessionMemoryStore sessionMemoryStore)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _contextBuilder = contextBuilder;
        _ragOptions = ragOptions.Value;
        _httpClientFactory = httpClientFactory;
        _openAiOptions = openAiOptions.Value;
        _sessionMemoryStore = sessionMemoryStore;
    }

    public async Task<QuestionResponse> ExecuteAsync(QuestionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new InvalidOperationException("A pergunta é obrigatória.");
        }

        if (string.IsNullOrWhiteSpace(request.Theme))
        {
            throw new InvalidOperationException("O Tema é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt) && string.IsNullOrWhiteSpace(request.ConversationId))
        {
            throw new InvalidOperationException("O Prompt de sistema obrigatório para iniciar uma conversa.");
        }

        string conversationId = request.ConversationId ?? "";
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            conversationId = Guid.NewGuid().ToString("N");
        }

        conversationId = _sessionMemoryStore.EnsureConversationId(conversationId);
        var historyTurns = _sessionMemoryStore.GetRecentTurns(conversationId, maxTurns: 10);
        var history = BuildHistory(historyTurns);

        var topK = NormalizeTopK(request.TopK ?? 5);
        IReadOnlyCollection<float> questionEmbedding;
        IReadOnlyCollection<RetrievedChunk> chunks;

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
            chunks = await _repository.SearchChunksAsync(
                request.Theme,
                questionEmbedding,
                topK,
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Elasticsearch indisponível para recuperar contexto.", ex);
        }

        var context = _contextBuilder.Build(chunks);
        var systemPrompt = request.SystemPrompt + @"
        Responda somente com base no bloco <CONTEXTO RECUPERADO>.
        Use o bloco <HISTORICO> apenas para continuidade da conversa, sem inventar dados fora o <CONTEXTO RECUPERADO>.
        Nunca invente informações e não use fontes externas.
        Se existir fonte da informação no contexto, adicionar ao da saída: ""Fonte da Informação: <link para a fonte da informação do contexto>""";

        string answer;
        string responseId;
        try
        {
            (answer, responseId) = await GenerateAnswerAsync(systemPrompt, context, request.Question, history, request.UserImageBase64, request.PreviousResponseId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Provedor de IA indisponível para gerar resposta.", ex);
        }

        _sessionMemoryStore.AppendTurn(conversationId, request.Question, answer);

        var citations = chunks.Select(c => new Citation(
            DocumentId: c.DocumentId,
            FileName: c.FileName,
            SectionPath: c.SectionPath,
            ChunkId: c.ChunkId,
            Snippet: c.Content.Length > 320 ? c.Content[..320] : c.Content,
            Score: c.Score,
            SourceUrl: c.SourceUrl)).ToArray();

        return new QuestionResponse(answer, citations, conversationId, responseId);
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

    private async Task<(string response, string responseId)> GenerateAnswerAsync(
        string systemPrompt,
        string context,
        string question,
        string? history,
        string? userImageBase64,
        string? previousResponseId,
        CancellationToken cancellationToken)
    {
        string responseId = "";

        var apiKey = !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey)
            ? _openAiOptions.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY nao configurada para chat completion.");
        }

        var model = string.IsNullOrWhiteSpace(_openAiOptions.ChatModel)
            ? "gpt-4o-mini"
            : _openAiOptions.ChatModel;

        var endpoint = (_openAiOptions.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/') + "/responses";

        bool temImagem = !string.IsNullOrWhiteSpace(userImageBase64);

        string? imageUrl = null;
        if (temImagem)
        {
            var raw = userImageBase64!.Trim();

            // Se já vier como data URL, mantém.
            imageUrl = raw.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                ? raw
                : $"data:image/png;base64,{raw}"; // troque png se necessário
        }

        var userPrompt = BuildUserPrompt(context, question, history);

        var userContent = !temImagem
           ? new object[]
           {
                        new
                        {
                            type = "input_text",
                            text = userPrompt
                        }
                    }
                    : new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = userPrompt
                        },
                        new
                        {
                            type = "input_image",
                            image_url = imageUrl
                        }
          };


        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["instructions"] = systemPrompt,
            //["temperature"] = 0.2,
            //["top_p"] = 0.2,
            ["max_output_tokens"] = 2048,
            ["input"] = new object[]
            {
                new
                {
                    role = "user",
                    content = userContent
                }
            }
        };



        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            payload["previous_response_id"] = previousResponseId;
        }


        var client = _httpClientFactory.CreateClient(nameof(OpenAiSemanticClient));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao gerar resposta na OpenAI. Status={(int)response.StatusCode} Body={responseBody}");
        }


        var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseBody);

        if (obj.TryGetProperty("id", out var idElement))
        {
            responseId = idElement.GetString() ?? "";
        }

        using var doc = JsonDocument.Parse(responseBody);
        var text = ExtractOutputText(doc.RootElement);
        return (string.IsNullOrWhiteSpace(text)
            ? "Nao foi possivel gerar resposta para a pergunta."
            : text, responseId);
    }

    private static string BuildUserPrompt(string context, string question, string? history)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<CONTEXTO RECUPERADO>");
        sb.AppendLine(context);
        sb.AppendLine("</CONTEXT RECUPERADO>");
        sb.AppendLine();
        sb.AppendLine("<HISTORICO>");
        sb.AppendLine(history ?? "");
        sb.AppendLine("</HISTORICO>");
        sb.AppendLine();
        sb.AppendLine("<PERGUNTA>");
        sb.AppendLine(question);
        sb.AppendLine("</PERGUNTA>");

        return sb.ToString();
    }

    private static string ExtractOutputText(JsonElement root)
    {
        var sb = new StringBuilder();

        if (root.TryGetProperty("output", out var outputArray)
            && outputArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var outputItem in outputArray.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var contentArray)
                    || contentArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in contentArray.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElement)
                        && typeElement.GetString() == "output_text"
                        && contentItem.TryGetProperty("text", out var textElement))
                    {
                        sb.Append(textElement.GetString());
                    }
                }
            }
        }

        if (sb.Length == 0
            && root.TryGetProperty("output_text", out var outputTextElement)
            && outputTextElement.ValueKind == JsonValueKind.String)
        {
            sb.Append(outputTextElement.GetString());
        }

        return sb.ToString();
    }

    private static string BuildHistory(IReadOnlyList<ConversationTurn> turns)
    {
        if (turns.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var turn in turns)
        {
            sb.AppendLine($"Usuário: {turn.UserQuestion}");
            sb.AppendLine($"Assistente: {turn.AssistantAnswer}");
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
