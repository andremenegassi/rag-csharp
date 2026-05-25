using IA.API.Application.Abstractions;
using IA.API.Domain.Entities;
using IA.API.Infrastructure.Options;
using IA.API.Infrastructure.Observability;
using IA.API.Infrastructure.Rag;
using Microsoft.Extensions.Options;

namespace IA.API.Application.Documents;

public sealed record UploadDocumentRequest(
    Stream FileStream,
    string FileName,
    string ContentType,
    string Theme,
    bool? ForceDeletions = false);

public sealed record UploadDocumentResult(string DocumentId, string Theme, string Status, int ChunkCount, bool? ForceDeletions = false);

public sealed class UploadDocumentUseCase
{
    private readonly IEnumerable<IDocumentParser> _parsers;
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRagRepository _repository;
    private readonly FileHashService _fileHashService;
    private readonly RagOptions _ragOptions;
    private readonly UploadOptions _uploadOptions;
    private readonly ILogger<UploadDocumentUseCase> _logger;

    public UploadDocumentUseCase(
        IEnumerable<IDocumentParser> parsers,
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IRagRepository repository,
        FileHashService fileHashService,
        IOptions<RagOptions> ragOptions,
        IOptions<UploadOptions> uploadOptions,
        ILogger<UploadDocumentUseCase> logger)
    {
        _parsers = parsers;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _repository = repository;
        _fileHashService = fileHashService;
        _ragOptions = ragOptions.Value;
        _uploadOptions = uploadOptions.Value;
        _logger = logger;
    }

    public async Task<UploadDocumentResult> ExecuteAsync(UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginRagScope("upload-document", request.Theme);
        ValidateRequest(request);

        var fileHash = await _fileHashService.ComputeSha256Async(request.FileStream, cancellationToken);

        if (request.ForceDeletions == true)
        {
            var deleted = await _repository.DeleteDocumentByFileNameAsync(request.Theme, request.FileName, cancellationToken);
            if (deleted)
            {
                _logger.LogInformation("Foram deletados documentos duplicados para o tema {Theme} devido à flag ForceDeletions.", request.Theme);
            }
        }
        else
        {
            _logger.LogInformation("Verificando existência de documento duplicado para o tema {Theme} e name {FileName}.", request.Theme, request.FileName);

            var exists = await _repository.ExistsDocumentByFileNameAsync(request.Theme, request.FileName, cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException("Documento duplicado para o tema informado.");
            }
        }

        request.FileStream.Position = 0;
        var parser = _parsers.FirstOrDefault(p => p.CanHandle(request.ContentType, request.FileName));
        if (parser is null)
        {
            throw new InvalidOperationException("Tipo de arquivo não suportado para ingestão.");
        }

        var parsed = await parser.ParseAsync(request.FileStream, request.FileName, request.ContentType, cancellationToken);
        var chunkInputs = parsed.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .Select(s => new ChunkInput(s.SectionPath, s.Content, s.SourceUrl, s.SourceText))
            .ToArray();

        var chunks = _chunkingService.Chunk(chunkInputs, _ragOptions.ChunkSize, _ragOptions.ChunkOverlap);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("Não foi possível gerar chunks para o documento informado.");
        }

        _logger.LogRagStep(
            step: "chunking",
            message: "Chunks gerados para o documento.",
            payload: new
            {
                request.FileName,
                request.Theme,
                ChunkCount = chunks.Count,
                _ragOptions.ChunkSize,
                _ragOptions.ChunkOverlap
            });

        var documentId = $"doc_{Guid.NewGuid():N}";
        var indexedChunks = await BuildDocumentChunksAsync(chunks.ToArray(), documentId, request, cancellationToken);

        var metadata = new DocumentMetadata(
            DocumentId: documentId,
            Theme: request.Theme,
            FileName: request.FileName,
            ContentType: request.ContentType,
            FileHash: fileHash,
            UploadedAt: DateTime.UtcNow,
            ChunkCount: indexedChunks.Count,
            Status: "Indexed");

        await _repository.IndexDocumentAsync(metadata, cancellationToken);
        await _repository.IndexChunksAsync(indexedChunks, cancellationToken);

        return new UploadDocumentResult(metadata.DocumentId, metadata.Theme, metadata.Status, metadata.ChunkCount);
    }

    private async Task<List<DocumentChunk>> BuildDocumentChunksAsync(
        ChunkResult[] chunks,
        string documentId,
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_ragOptions.EmbeddingBatchSize, 1, 2048);
        var maxConcurrency = Math.Clamp(_ragOptions.EmbeddingMaxConcurrency, 1, 64);
        var totalBatches = (int)Math.Ceiling(chunks.Length / (double)batchSize);
        var chunkSlots = new DocumentChunk?[chunks.Length];
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var embeddingStopwatch = System.Diagnostics.Stopwatch.StartNew();

        var batchTasks = Enumerable.Range(0, totalBatches).Select(async batchIndex =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var offset = batchIndex * batchSize;
                var count = Math.Min(batchSize, chunks.Length - offset);
                var chunkBatch = chunks.AsSpan(offset, count).ToArray();
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                    chunkBatch.Select(static c => c.Content).ToArray(),
                    cancellationToken);

                for (var i = 0; i < count; i++)
                {
                    var chunk = chunkBatch[i];
                    var embedding = i < embeddings.Count ? embeddings[i] : Array.Empty<float>();
                    chunkSlots[offset + i] = new DocumentChunk(
                        ChunkId: chunk.ChunkId,
                        ChunkOrder: chunk.ChunkOrder,
                        DocumentId: documentId,
                        Theme: request.Theme,
                        Content: chunk.Content,
                        Embedding: embedding,
                        SectionPath: chunk.SectionPath,
                        SourceUrl: chunk.SourceUrl,
                        SourceText: chunk.SourceText,
                        FileName: request.FileName,
                        ContentType: request.ContentType,
                        CreatedAt: DateTime.UtcNow,
                        Title: chunk.Title,
                        Subtitle: chunk.Subtitle);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(batchTasks);
        embeddingStopwatch.Stop();

        var embeddedChunks = chunkSlots
            .Select((chunk, index) => chunk ?? throw new InvalidOperationException($"Chunk index {index} nao recebeu embedding."))
            .ToList();

        _logger.LogRagStep(
            step: "embedding",
            message: "Embeddings gerados para os chunks.",
            payload: new
            {
                request.FileName,
                request.Theme,
                ChunkCount = chunks.Length,
                BatchSize = batchSize,
                MaxConcurrency = maxConcurrency,
                TotalBatches = totalBatches,
                ElapsedMs = embeddingStopwatch.ElapsedMilliseconds,
                ChunksPerSecond = chunks.Length / Math.Max(embeddingStopwatch.Elapsed.TotalSeconds, 0.001)
            });

        return embeddedChunks;
    }

    private void ValidateRequest(UploadDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Theme))
        {
            throw new InvalidOperationException("O Tema é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new InvalidOperationException("Nome de arquivo inválido.");
        }

        var extension = Path.GetExtension(request.FileName);
        var allowed = _uploadOptions.AllowedExtensions ?? [];
        if (!allowed.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Extensão de arquivo não permitida.");
        }
    }
}
