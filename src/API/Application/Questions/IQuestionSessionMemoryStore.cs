namespace IA.API.Application.Questions;

public interface IQuestionSessionMemoryStore
{
    string EnsureConversationId(string? conversationId);
    IReadOnlyList<ConversationTurn> GetRecentTurns(string conversationId, int maxTurns);
    void AppendTurn(string conversationId, string userQuestion, string assistantAnswer);
}

public sealed record ConversationTurn(string UserQuestion, string AssistantAnswer, DateTime TimestampUtc);
