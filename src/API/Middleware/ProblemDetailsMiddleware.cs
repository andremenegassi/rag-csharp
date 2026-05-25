using System.Net;
using System.Text.Json;

namespace IA.API.Middleware;

public sealed class ProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProblemDetailsMiddleware> _logger;

    public ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado durante processamento da requisição.");
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError, "Erro interno no servidor.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, HttpStatusCode statusCode, string title)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = "about:blank",
            title,
            status = (int)statusCode,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
