namespace IA.API.Domain.Entities;

public sealed record ChatRequest(
    string? SystemPrompt,
    string? UserPrompt,
    string? UserImageBase64,
    string? PreviousResponseId);
    


public sealed record ChatResponse(
        string Answer,
        string? ResponseId);
 
 