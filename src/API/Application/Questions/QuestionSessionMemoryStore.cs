using Microsoft.Extensions.Caching.Memory;

namespace IA.API.Application.Questions;

public sealed class QuestionSessionMemoryStore : IQuestionSessionMemoryStore
{
    private const int MaxTurnsPerSession = 30;
    private static readonly TimeSpan SessionExpiration = TimeSpan.FromHours(24);
    private readonly IMemoryCache _memoryCache;

    public QuestionSessionMemoryStore(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public string EnsureConversationId(string? conversationId)
    {
        return string.IsNullOrWhiteSpace(conversationId)
            ? Guid.NewGuid().ToString("N")
            : conversationId.Trim();
    }

    public IReadOnlyList<ConversationTurn> GetRecentTurns(string conversationId, int maxTurns)
    {
        var state = GetState(conversationId);

        lock (state.SyncRoot)
        {
            return state.Turns.TakeLast(Math.Max(1, maxTurns)).ToArray();
        }
    }

    public void AppendTurn(string conversationId, string userQuestion, string assistantAnswer)
    {
        var state = GetState(conversationId);

        lock (state.SyncRoot)
        {
            state.Turns.Add(new ConversationTurn(userQuestion, assistantAnswer, DateTime.UtcNow));

            if (state.Turns.Count > MaxTurnsPerSession)
            {
                var removeCount = state.Turns.Count - MaxTurnsPerSession;
                state.Turns.RemoveRange(0, removeCount);
            }
        }
    }

    private SessionState GetState(string conversationId)
    {
        var key = $"question-session:{conversationId}";
        return _memoryCache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SessionExpiration;
            return new SessionState();
        })!;
    }

    private sealed class SessionState
    {
        public object SyncRoot { get; } = new();
        public List<ConversationTurn> Turns { get; } = new();
    }
}
