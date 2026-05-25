namespace IA.API.Domain.Entities;

public sealed record DocumentMetadata(
    string DocumentId,
    string Theme,
    string FileName,
    string ContentType,
    string FileHash,
    DateTime UploadedAt,
    int ChunkCount,
    string Status);

public sealed record DocumentChunk(
    string ChunkId,
    int ChunkOrder,
    string DocumentId,
    string Theme,
    string Content,
    IReadOnlyCollection<float> Embedding,
    string SectionPath,
    string? SourceUrl,
    string? SourceText,
    string FileName,
    string ContentType,
    DateTime CreatedAt,
    string? Title = null,
    string? Subtitle = null);

public sealed record Citation(
    string DocumentId,
    string FileName,
    string SectionPath,
    string ChunkId,
    string Snippet,
    double Score,
    string? SourceUrl);

public sealed record RetrievedChunk(
    string ChunkId,
    int ChunkOrder,
    string DocumentId,
    string Theme,
    string Content,
    string SectionPath,
    string FileName,
    string? SourceUrl,
    double Score);

/// <summary>Representa uma solicitação de pergunta.</summary>
/// <param name="SystemPrompt">Prompt para guiar o agente. Necessário apenas na primeira pergunta.</param>
/// <param name="Question">Pergunta a ser respondida pelo agente.</param>
/// <param name="Theme">Tema relacionado à pergunta.</param>
/// <param name="UserImageBase64">Imagem do usuário em formato Base64, opcional para fornecer contexto visual à pergunta.</param>
/// <param name="TopK">Número de chunks relevantes a serem recuperados para responder à pergunta. Padrão é 5.</param>
/// <param name="ConversationId">ID opcional da conversa para manter o contexto entre várias perguntas.</param>
/// <param name="PreviousResponseId">ID da resposta anterior para manter o contexto entre várias perguntas.</param>
public sealed record QuestionRequest(
    string? SystemPrompt = "",
    string? Question = "",
    string? Theme = "",
    string? UserImageBase64 = null,
    int? TopK = 5,
    string? ConversationId = null,
    string? PreviousResponseId = null);

public sealed record SearchQuestionChunksRequest(
    string? Question = "",
    string? Theme = null,
    int? TopK = 5);


/// <summary>
/// Representa uma resposta a uma pergunta.
/// </summary>
/// <param name="Answer">Resposta gerada pelo agente.</param>
/// <param name="Citations">Citações relevantes utilizadas para gerar a resposta.</param>
/// <param name="ConversationId">ID da conversa para manter o contexto entre várias perguntas.</param>
/// <param name="PreviousResponseId">ID da resposta anterior para manter o contexto entre várias perguntas.</param>
public sealed record QuestionResponse(
    string Answer,
    IReadOnlyCollection<Citation> Citations,
    string ConversationId,
    string? PreviousResponseId = null);
