using IA.API.Application.Abstractions;
using IA.API.Application.Documents;
using IA.API.Infrastructure.Rag;
using Microsoft.AspNetCore.Mvc;

namespace IA.API.Controllers;

/// <summary>
/// Endpoints para gestão de documentos no contexto RAG, incluindo upload imediato, fila e consulta.
/// </summary>
[ApiController]
[Route("/assistent/documents", Name = "Documents")]
public sealed class DocumentsController : ControllerBase
{           
    private const long MaxUploadRequestSizeBytes = 500 * 1024 * 1024;

    private readonly UploadDocumentUseCase _uploadDocumentUseCase;
    private readonly IRagRepository _repository;
    private readonly IDocumentUploadQueue _uploadQueue;
    private readonly FileHashService _fileHashService;

    public DocumentsController(
        UploadDocumentUseCase uploadDocumentUseCase,
        IRagRepository repository,
        IDocumentUploadQueue uploadQueue,
        FileHashService fileHashService)
    {
        _uploadDocumentUseCase = uploadDocumentUseCase;
        _repository = repository;
        _uploadQueue = uploadQueue;
        _fileHashService = fileHashService;
    }

    /// <summary>
    /// Faz o upload de um documento e processa o conteúdo imediatamente para indexação no tema informado.
    /// </summary>
    /// <param name="file">Arquivo a ser processado.</param>
    /// <param name="theme">Tema utilizado para agrupar e consultar o documento.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna os dados do documento criado ou um erro de validação/conflito.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxUploadRequestSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestSizeBytes)]
    [ProducesResponseType(typeof(UploadDocumentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string theme, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Arquivo obrigatório." });
        }

        await using var stream = file.OpenReadStream();

        try
        {
            var result = await _uploadDocumentUseCase.ExecuteAsync(new UploadDocumentRequest(stream, file.FileName, file.ContentType, theme), cancellationToken);
            return Created($"/documents/{result.DocumentId}", result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("duplicado", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Enfileira um documento para processamento assíncrono, evitando duplicidade na fila por tema.
    /// </summary>
    /// <param name="file">Arquivo a ser enfileirado.</param>
    /// <param name="theme">Tema associado ao documento.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna os dados do item enfileirado ou conflito quando já existir pendência equivalente.</returns>
    [HttpPost("queue")]
    [RequestSizeLimit(MaxUploadRequestSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestSizeBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnqueueUpload([FromForm] IFormFile file, [FromForm] string theme, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Arquivo obrigatório." });
        }

        await using var sourceStream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await sourceStream.CopyToAsync(memory, cancellationToken);
        var content = memory.ToArray();

        await using var hashStream = new MemoryStream(content, writable: false);
        var fileHash = await _fileHashService.ComputeSha256Async(hashStream, cancellationToken);

        var item = QueuedDocumentItem.Create(theme, file.FileName, file.ContentType, fileHash, content);
        var enqueued = await _uploadQueue.EnqueueAsync(item, cancellationToken);

        if (!enqueued)
        {
            return Conflict(new { message = "Documento já está pendente na fila para o tema informado." });
        }

        return Accepted(new
        {
            item.QueueId,
            item.Theme,
            item.FileName,
            Status = item.Status.ToString(),
            item.EnqueuedAt
        });
    }

    /// <summary>
    /// Lista os itens de upload que ainda estão pendentes na fila de processamento.
    /// </summary>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna a coleção de documentos pendentes na fila.</returns>
    [HttpGet("queue/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueuePending(CancellationToken cancellationToken = default)
    {
        var items = await _uploadQueue.GetPendingAsync(cancellationToken);
        return Ok(items);
    }

    /// <summary>
    /// Lista os documentos indexados, com opção de filtrar por tema.
    /// </summary>
    /// <param name="theme">Tema opcional para filtrar os documentos.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna os documentos cadastrados no repositório.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? theme, CancellationToken cancellationToken = default)
    {
        var documents = await _repository.ListDocumentsAsync(theme, cancellationToken);
        return Ok(documents);
    }

    /// <summary>
    /// Obtém um documento específico pelo identificador.
    /// </summary>
    /// <param name="id">Identificador do documento.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna o documento encontrado ou 404 quando inexistente.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetDocumentByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    /// <summary>
    /// Remove um documento de um tema específico.
    /// </summary>
    /// <param name="id">Identificador do documento.</param>
    /// <param name="theme">Tema ao qual o documento pertence.</param>
    /// <param name="cancellationToken">Token para cancelamento da operação.</param>
    /// <returns>Retorna sucesso sem conteúdo ou erro de validação/não encontrado.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, [FromQuery] string theme, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return BadRequest(new { message = "O Tema é obrigatório." });
        }

        try
        {
            var deleted = await _repository.DeleteDocumentAsync(theme, id, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { message = "Documento não encontrado para o tema informado." });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
