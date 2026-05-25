using IA.API.Application.Questions;
using IA.API.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace IA.API.Controllers;

/// <summary>
/// Endpoints para perguntas sobre a base RAG e consulta direta de trechos relevantes.
/// </summary>
[ApiController]
[Route("/assistent/questions", Name = "Questions")]
public sealed class QuestionsController : ControllerBase
{
    private readonly AskQuestionUseCase _askQuestionUseCase;
    private readonly SearchQuestionChunksUseCase _searchQuestionChunksUseCase;

    public QuestionsController(
        AskQuestionUseCase askQuestionUseCase,
        SearchQuestionChunksUseCase searchQuestionChunksUseCase)
    {
        _askQuestionUseCase = askQuestionUseCase;
        _searchQuestionChunksUseCase = searchQuestionChunksUseCase;
    }

    /// <summary>
    /// Processa uma pergunta no contexto RAG e retorna a resposta gerada pela IA.
    /// </summary>
    /// <param name="request">Dados da pergunta e contexto necessário para consulta.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna a resposta da pergunta ou erro de validação/indisponibilidade.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(QuestionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ask([FromBody] QuestionRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest();
        }

        try
        {
            var response = await _askQuestionUseCase.ExecuteAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Elasticsearch indisponível", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Provedor de IA indisponível", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Busca os chunks mais relevantes para uma pergunta sem gerar resposta final.
    /// </summary>
    /// <param name="request">Dados da consulta para recuperação semântica dos trechos.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna a coleção de chunks recuperados ou erro de validação/indisponibilidade.</returns>
    [HttpPost("chunks")]
    [ProducesResponseType(typeof(IReadOnlyCollection<RetrievedChunk>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SearchChunks([FromBody] SearchQuestionChunksRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest();
        }

        try
        {
            var response = await _searchQuestionChunksUseCase.ExecuteAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Elasticsearch indisponível", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Provedor de IA indisponível", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
