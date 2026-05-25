namespace IA.API.Infrastructure.Observability;

public static class RagLogging
{
    public static IDisposable BeginRagScope(this ILogger logger, string operation, string? theme = null, string? documentId = null)
    {
        var scope = new Dictionary<string, object?>
        {
            ["rag.operation"] = operation,
            ["rag.theme"] = theme,
            ["rag.documentId"] = documentId
        };

        return logger.BeginScope(scope)!;
    }

    public static void LogRagStep(this ILogger logger, string step, string message, object? payload = null)
    {
        logger.LogInformation("RAG step={Step} message={Message} payload={Payload}", step, message, payload);
    }

    public static void LogRagFailure(this ILogger logger, string step, Exception exception, object? payload = null)
    {
        logger.LogError(exception, "RAG failure step={Step} payload={Payload}", step, payload);
    }
}
