using System.Text;
using System.Text.Json;
using IA.API.Application.Abstractions;
using IA.API.Domain.Entities;
using IA.API.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace IA.API.Infrastructure.Elasticsearch;

public sealed class RagElasticsearchRepository : IRagRepository
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ElasticsearchOptions _options;
    private readonly RagOptions _ragOptions;

    public RagElasticsearchRepository(
        IHttpClientFactory httpClientFactory,
        IOptions<ElasticsearchOptions> options,
        IOptions<RagOptions> ragOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _ragOptions = ragOptions.Value;
    }

    public async Task<bool> ExistsDocumentByHashAsync(string theme, string fileHash, CancellationToken cancellationToken)
    {
        var documentsIndex = GetThemedDocumentsIndex(theme);
        var normalizedHash = fileHash?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedHash))
        {
            return false;
        }

        var query = new
        {
            size = 1,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                        new
                        {
                            @bool = new
                            {
                                should = new object[]
                                {
                                    new { term = new { fileHash = normalizedHash } },
                                    new { term = new Dictionary<string, object> { ["FileHash.keyword"] = normalizedHash } },
                                    new { term = new Dictionary<string, object> { ["FileHash"] = normalizedHash } }
                                },
                                minimum_should_match = 1
                            }
                        }
                    }
                }
            }
        };

        using var response = await PostJsonAsync($"/{documentsIndex}/_search", query, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao consultar duplicidade no Elasticsearch: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("hits", out var hits))
        {
            return false;
        }

        if (hits.TryGetProperty("total", out var totalElement))
        {
            if (totalElement.ValueKind == JsonValueKind.Object &&
                totalElement.TryGetProperty("value", out var valueElement) &&
                valueElement.TryGetInt32(out var valueFromObject))
            {
                return valueFromObject > 0;
            }

            if (totalElement.ValueKind == JsonValueKind.Number && totalElement.TryGetInt32(out var valueFromNumber))
            {
                return valueFromNumber > 0;
            }
        }

        return false;
    }


    public async Task<bool> ExistsDocumentByFileNameAsync(string theme, string fileName, CancellationToken cancellationToken)
    {
        var documentsIndex = GetThemedDocumentsIndex(theme);
        var normalizedName = fileName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var query = new
        {
            size = 1,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                    new
                    {
                        @bool = new
                        {
                            should = new object[]
                            {
                                new { term = new { fileName = normalizedName } },
                                new { term = new Dictionary<string, object> { ["FileName.keyword"] = normalizedName } },
                                new { term = new Dictionary<string, object> { ["FileName"] = normalizedName } }
                            },
                            minimum_should_match = 1
                        }
                    }
                    }
                }
            }
        };

        using var response = await PostJsonAsync($"/{documentsIndex}/_search", query, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao consultar duplicidade no Elasticsearch: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("hits", out var hits))
        {
            return false;
        }

        if (hits.TryGetProperty("total", out var totalElement))
        {
            if (totalElement.ValueKind == JsonValueKind.Object &&
                totalElement.TryGetProperty("value", out var valueElement) &&
                valueElement.TryGetInt32(out var valueFromObject))
            {
                return valueFromObject > 0;
            }

            if (totalElement.ValueKind == JsonValueKind.Number && totalElement.TryGetInt32(out var valueFromNumber))
            {
                return valueFromNumber > 0;
            }
        }

        return false;
    }



    public async Task<bool> DeleteDocumentAsync(string theme, string documentId, CancellationToken cancellationToken)
    {
        var normalizedDocumentId = documentId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedDocumentId))
        {
            throw new InvalidOperationException("DocumentId e obrigatorio.");
        }

        var documentsIndex = GetThemedDocumentsIndex(theme);
        var chunksIndex = GetThemedChunksIndex(theme);

        var client = _httpClientFactory.CreateClient(nameof(RagElasticsearchRepository));
        using var deleteDocumentResponse = await client.SendAsync(
            CreateRequest(HttpMethod.Delete, $"/{documentsIndex}/_doc/{normalizedDocumentId}"),
            cancellationToken);

        if (deleteDocumentResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        var deleteDocumentBody = await deleteDocumentResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!deleteDocumentResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir documento no Elasticsearch: {deleteDocumentBody}");
        }

        var deleteChunksQuery = new
        {
            query = new
            {
                @bool = new
                {
                    should = new object[]
                    {
                        new { term = new { documentId = normalizedDocumentId } },
                        new { term = new Dictionary<string, object> { ["DocumentId.keyword"] = normalizedDocumentId } },
                        new { term = new Dictionary<string, object> { ["DocumentId"] = normalizedDocumentId } }
                    },
                    minimum_should_match = 1
                }
            }
        };

        using var deleteChunksResponse = await PostJsonAsync($"/{chunksIndex}/_delete_by_query", deleteChunksQuery, cancellationToken);
        var deleteChunksBody = await deleteChunksResponse.Content.ReadAsStringAsync(cancellationToken);
        if (deleteChunksResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }

        if (!deleteChunksResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir chunks no Elasticsearch: {deleteChunksBody}");
        }

        return true;
    }


    public async Task<bool> DeleteDocumentByHashAsync(string theme, string fileHash, CancellationToken cancellationToken)
    {
        var normalizedHash = fileHash?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedHash))
        {
            throw new InvalidOperationException("FileHash e obrigatorio.");
        }

        var documentsIndex = GetThemedDocumentsIndex(theme);
        var chunksIndex = GetThemedChunksIndex(theme);

        // 1) Busca documentos por hash (mesma ideia do ExistsDocumentByHashAsync)
        var findDocumentsQuery = new
        {
            size = 500,
            _source = false,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                    new
                    {
                        @bool = new
                        {
                            should = new object[]
                            {
                                new { term = new { fileHash = normalizedHash } },
                                new { term = new Dictionary<string, object> { ["FileHash.keyword"] = normalizedHash } },
                                new { term = new Dictionary<string, object> { ["FileHash"] = normalizedHash } }
                            },
                            minimum_should_match = 1
                        }
                    }
                    }
                }
            }
        };

        using var findDocumentsResponse = await PostJsonAsync($"/{documentsIndex}/_search", findDocumentsQuery, cancellationToken);
        var findDocumentsBody = await findDocumentsResponse.Content.ReadAsStringAsync(cancellationToken);

        if (findDocumentsResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!findDocumentsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao buscar documento por hash no Elasticsearch: {findDocumentsBody}");
        }

        using var findDocumentsJson = JsonDocument.Parse(findDocumentsBody);
        if (!findDocumentsJson.RootElement.TryGetProperty("hits", out var hitsRoot)
            || !hitsRoot.TryGetProperty("hits", out var hits))
        {
            return false;
        }

        var documentIds = hits.EnumerateArray()
            .Select(hit => hit.TryGetProperty("_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (documentIds.Length == 0)
        {
            return false;
        }

        // 2) Exclui documentos encontrados
        var deleteDocumentsQuery = new
        {
            query = new
            {
                ids = new
                {
                    values = documentIds
                }
            }
        };

        using var deleteDocumentsResponse = await PostJsonAsync($"/{documentsIndex}/_delete_by_query", deleteDocumentsQuery, cancellationToken);
        var deleteDocumentsBody = await deleteDocumentsResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!deleteDocumentsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir documentos por hash no Elasticsearch: {deleteDocumentsBody}");
        }

        // 3) Exclui chunks dos documentos removidos
        var deleteChunksQuery = new
        {
            query = new
            {
                @bool = new
                {
                    should = new object[]
                    {
                    new { terms = new { documentId = documentIds } },
                    new { terms = new Dictionary<string, object> { ["DocumentId.keyword"] = documentIds } },
                    new { terms = new Dictionary<string, object> { ["DocumentId"] = documentIds } }
                    },
                    minimum_should_match = 1
                }
            }
        };

        using var deleteChunksResponse = await PostJsonAsync($"/{chunksIndex}/_delete_by_query", deleteChunksQuery, cancellationToken);
        var deleteChunksBody = await deleteChunksResponse.Content.ReadAsStringAsync(cancellationToken);

        if (deleteChunksResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }

        if (!deleteChunksResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir chunks por hash no Elasticsearch: {deleteChunksBody}");
        }

        return true;
    }


    public async Task<bool> DeleteDocumentByFileNameAsync(string theme, string fileName, CancellationToken cancellationToken)
    {
        var normalizedFileName = fileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            throw new InvalidOperationException("FileName e obrigatorio.");
        }

        var documentsIndex = GetThemedDocumentsIndex(theme);
        var chunksIndex = GetThemedChunksIndex(theme);

        // 1) Busca documentos por fileName (mesma ideia do ExistsDocumentByFileNameAsync)
        var findDocumentsQuery = new
        {
            size = 500,
            _source = false,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                new
                {
                    @bool = new
                    {
                        should = new object[]
                        {
                            new { term = new { fileName = normalizedFileName } },
                            new { term = new Dictionary<string, object> { ["FileName.keyword"] = normalizedFileName } },
                            new { term = new Dictionary<string, object> { ["FileName"] = normalizedFileName } }
                        },
                        minimum_should_match = 1
                    }
                }
                    }
                }
            }
        };

        using var findDocumentsResponse = await PostJsonAsync($"/{documentsIndex}/_search", findDocumentsQuery, cancellationToken);
        var findDocumentsBody = await findDocumentsResponse.Content.ReadAsStringAsync(cancellationToken);

        if (findDocumentsResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!findDocumentsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao buscar documento por hash no Elasticsearch: {findDocumentsBody}");
        }

        using var findDocumentsJson = JsonDocument.Parse(findDocumentsBody);
        if (!findDocumentsJson.RootElement.TryGetProperty("hits", out var hitsRoot)
            || !hitsRoot.TryGetProperty("hits", out var hits))
        {
            return false;
        }

        var documentIds = hits.EnumerateArray()
            .Select(hit => hit.TryGetProperty("_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (documentIds.Length == 0)
        {
            return false;
        }

        // 2) Exclui documentos encontrados
        var deleteDocumentsQuery = new
        {
            query = new
            {
                ids = new
                {
                    values = documentIds
                }
            }
        };

        using var deleteDocumentsResponse = await PostJsonAsync($"/{documentsIndex}/_delete_by_query", deleteDocumentsQuery, cancellationToken);
        var deleteDocumentsBody = await deleteDocumentsResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!deleteDocumentsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir documentos por hash no Elasticsearch: {deleteDocumentsBody}");
        }

        // 3) Exclui chunks dos documentos removidos
        var deleteChunksQuery = new
        {
            query = new
            {
                @bool = new
                {
                    should = new object[]
                    {
                new { terms = new { documentId = documentIds } },
                new { terms = new Dictionary<string, object> { ["DocumentId.keyword"] = documentIds } },
                new { terms = new Dictionary<string, object> { ["DocumentId"] = documentIds } }
                    },
                    minimum_should_match = 1
                }
            }
        };

        using var deleteChunksResponse = await PostJsonAsync($"/{chunksIndex}/_delete_by_query", deleteChunksQuery, cancellationToken);
        var deleteChunksBody = await deleteChunksResponse.Content.ReadAsStringAsync(cancellationToken);

        if (deleteChunksResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }

        if (!deleteChunksResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao excluir chunks por fileName no Elasticsearch: {deleteChunksBody}");
        }

        return true;
    }


    public async Task IndexDocumentAsync(DocumentMetadata document, CancellationToken cancellationToken)
    {
        var documentsIndex = GetThemedDocumentsIndex(document.Theme);
        using var response = await PutJsonAsync($"/{documentsIndex}/_doc/{document.DocumentId}", document, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao indexar documento no Elasticsearch: {body}");
        }
    }

    public async Task IndexChunksAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var chunksIndex = GetThemedChunksIndex(chunks.First().Theme);
        if (chunks.Any(c => !string.Equals(GetThemedChunksIndex(c.Theme), chunksIndex, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Nao e permitido indexar chunks de temas diferentes no mesmo lote.");
        }

        var maxBatchBytes = Math.Clamp(_ragOptions.MaxBulkRequestBytes, 1024 * 1024, 50 * 1024 * 1024);
        var client = _httpClientFactory.CreateClient(nameof(RagElasticsearchRepository));

        var lines = new List<string>(chunks.Count * 2);
        var currentBatchBytes = 0;

        async Task FlushAsync()
        {
            if (lines.Count == 0)
            {
                return;
            }

            var payload = string.Concat(lines);

            using var request = CreateRequest(HttpMethod.Post, "/_bulk");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/x-ndjson");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var safeBody = string.IsNullOrWhiteSpace(body) ? "<empty-body>" : body;
                throw new InvalidOperationException(
                    $"Falha ao indexar chunks no Elasticsearch. " +
                    $"Status={(int)response.StatusCode} ({response.ReasonPhrase}), " +
                    $"PayloadBytes={Encoding.UTF8.GetByteCount(payload)}, Body={safeBody}");
            }

            // Elasticsearch bulk can return 200 with partial failures.
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("errors", out var errorsEl) &&
                    errorsEl.ValueKind == JsonValueKind.True)
                {
                    throw new InvalidOperationException(
                        $"Falha parcial no _bulk do Elasticsearch: {body}");
                }
            }

            lines.Clear();
            currentBatchBytes = 0;
        }

        foreach (var chunk in chunks)
        {
            var actionLine = JsonSerializer.Serialize(new { index = new { _index = chunksIndex, _id = chunk.ChunkId } }) + "\n";
            var sourceLine = JsonSerializer.Serialize(chunk) + "\n";
            var entryBytes = Encoding.UTF8.GetByteCount(actionLine) + Encoding.UTF8.GetByteCount(sourceLine);

            if (currentBatchBytes > 0 && currentBatchBytes + entryBytes > maxBatchBytes)
            {
                await FlushAsync();
            }

            lines.Add(actionLine);
            lines.Add(sourceLine);
            currentBatchBytes += entryBytes;
        }

        await FlushAsync();
    }

    public async Task<IReadOnlyCollection<DocumentMetadata>> ListDocumentsAsync(string? theme, CancellationToken cancellationToken)
    {
        var query = new { size = 200, query = new { match_all = new { } } };
        var indexPattern = string.IsNullOrWhiteSpace(theme)
            ? $"{_options.DocumentsIndex}-*"
            : GetThemedDocumentsIndex(theme);

        using var response = await PostJsonAsync($"/{indexPattern}/_search", query, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<DocumentMetadata>();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao listar documentos no Elasticsearch: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var hits = doc.RootElement.GetProperty("hits").GetProperty("hits"). EnumerateArray();
        var items = new List<DocumentMetadata>();

        foreach (var hit in hits)
        {
            var source = hit.GetProperty("_source");
            items.Add(new DocumentMetadata(
                DocumentId: GetStringProperty(source, "documentId", "DocumentId"),
                Theme: GetStringProperty(source, "theme", "Theme"),
                FileName: GetStringProperty(source, "fileName", "FileName"),
                ContentType: GetStringProperty(source, "contentType", "ContentType"),
                FileHash: GetStringProperty(source, "fileHash", "FileHash"),
                UploadedAt: GetDateTimeProperty(source, "uploadedAt", "UploadedAt"),
                ChunkCount: GetInt32Property(source, "chunkCount", "ChunkCount"),
                Status: GetStringProperty(source, "status", "Status")));
        }

        return items;
    }

    public async Task<DocumentMetadata?> GetDocumentByIdAsync(string documentId, CancellationToken cancellationToken)
    {
        var query = new
        {
            size = 1,
            query = new
            {
                ids = new
                {
                    values = new[] { documentId }
                }
            }
        };

        using var response = await PostJsonAsync($"/{_options.DocumentsIndex}-*/_search", query, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao obter documento no Elasticsearch: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("hits", out var hitsRoot)
            || !hitsRoot.TryGetProperty("hits", out var hits)
            || hits.GetArrayLength() == 0)
        {
            return null;
        }

        var source = hits[0].GetProperty("_source");
        return new DocumentMetadata(
            DocumentId: GetStringProperty(source, "documentId", "DocumentId"),
            Theme: GetStringProperty(source, "theme", "Theme"),
            FileName: GetStringProperty(source, "fileName", "FileName"),
            ContentType: GetStringProperty(source, "contentType", "ContentType"),
            FileHash: GetStringProperty(source, "fileHash", "FileHash"),
            UploadedAt: GetDateTimeProperty(source, "uploadedAt", "UploadedAt"),
            ChunkCount: GetInt32Property(source, "chunkCount", "ChunkCount"),
            Status: GetStringProperty(source, "status", "Status"));
    }

    public async Task<IReadOnlyCollection<string>> ListThemesAsync(CancellationToken cancellationToken)
    {   
        var themes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var theme in await QueryThemesByIndexAsync($"{_options.DocumentsIndex}-*", cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(theme))
            {
                themes.Add(theme);
            }
        }

        foreach (var theme in await QueryThemesByIndexAsync($"{_options.ChunksIndex}-*", cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(theme))
            {
                themes.Add(theme);
            }
        }

        return themes.OrderBy(t => t).ToArray();
    }

    private async Task<IReadOnlyCollection<string>> QueryThemesByIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        var themes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in new[] { "theme", "Theme.keyword", "Theme" })
        {
            foreach (var theme in await QueryThemesByFieldAsync(indexName, field, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(theme))
                {
                    themes.Add(theme);
                }
            }
        }

        return themes.ToArray();
    }

    private async Task<IReadOnlyCollection<string>> QueryThemesByFieldAsync(string indexName, string fieldName, CancellationToken cancellationToken)
    {
        var query = new
        {
            size = 0,
            aggs = new
            {
                themes = new
                {
                    terms = new
                    {
                        field = fieldName,
                        size = 200
                    }
                }   
            }
        };

        using var response = await PostJsonAsync($"/{indexName}/_search", query, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("aggregations", out var aggregations)
            || !aggregations.TryGetProperty("themes", out var themes)
            || !themes.TryGetProperty("buckets", out var buckets))
        {
            return Array.Empty<string>();
        }

        return buckets.EnumerateArray()
            .Select(b => b.GetProperty("key").GetString() ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<RetrievedChunk>> SearchChunksAsync(
        string theme,
        IReadOnlyCollection<float> questionEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var normalizedTheme = theme?.Trim() ?? string.Empty;
        var themedChunksIndex = string.IsNullOrWhiteSpace(normalizedTheme)
            ? $"{_options.ChunksIndex}-*"
            : GetThemedChunksIndex(normalizedTheme);
        var neighborDistance = Math.Max(0, _ragOptions.NeighborDistance);

        var fetchK = Math.Max(topK * 3, 10);                 // busca mais candidatos
        var numCandidates = Math.Max(fetchK * 4, 20);       // recall mais alto
        var filter = new List<object>();

        foreach (var embeddingField in new[] { "embedding", "Embedding" })
        {
            var query = new
            {
                size = fetchK,
                knn = new
                {
                    field = embeddingField,
                    query_vector = questionEmbedding,
                    k = fetchK,
                    num_candidates = numCandidates,
                    filter
                }
            };


            using var response = await PostJsonAsync($"/{themedChunksIndex}/_search", query, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Array.Empty<RetrievedChunk>();
            }

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("hits", out var hitsRoot)
                || !hitsRoot.TryGetProperty("hits", out var hits))
            {
                continue;
            }

            var items = new List<RetrievedChunk>();
            foreach (var hit in hits.EnumerateArray())
            {
                var source = hit.GetProperty("_source");
                var score = hit.TryGetProperty("_score", out var scoreElement) ? scoreElement.GetDouble() : 0d;
                var chunkOrder = TryGetInt32Property(source, out var parsedChunkOrder, "chunkOrder", "ChunkOrder")
                    ? parsedChunkOrder
                    : -1;

                items.Add(new RetrievedChunk(
                    ChunkId: GetStringProperty(source, "chunkId", "ChunkId"),
                    ChunkOrder: chunkOrder,
                    DocumentId: GetStringProperty(source, "documentId", "DocumentId"),
                    Theme: GetStringProperty(source, "theme", "Theme"),
                    Content: GetStringProperty(source, "content", "Content"),
                    SectionPath: GetStringProperty(source, "sectionPath", "SectionPath"),
                    FileName: GetStringProperty(source, "fileName", "FileName"),
                    SourceUrl: GetNullableStringProperty(source, "sourceUrl", "SourceUrl"),
                    Score: score));
            }

            if (items.Count > 0)
            {

                var primary = items
                    .OrderByDescending(i => i.Score)
                    .ToArray();

                return await ExpandWithNeighborChunksAsync(
                    themedChunksIndex,
                    primary,
                    neighborDistance,
                    cancellationToken);
            }
        }

        return Array.Empty<RetrievedChunk>();
    }

    private async Task<IReadOnlyCollection<RetrievedChunk>> ExpandWithNeighborChunksAsync(
        string themedChunksIndex,
        IReadOnlyCollection<RetrievedChunk> seeds,
        int neighborDistance,
        CancellationToken cancellationToken)
    {
        if (seeds.Count == 0)
        {
            return Array.Empty<RetrievedChunk>();
        }

        if (neighborDistance <= 0)
        {
            return seeds.ToArray();
        }

        var result = new List<RetrievedChunk>();
        var seenChunkIds = new HashSet<string>(StringComparer.Ordinal);

        void AddUnique(RetrievedChunk chunk)
        {
            if (!string.IsNullOrWhiteSpace(chunk.ChunkId) && seenChunkIds.Add(chunk.ChunkId))
            {
                result.Add(chunk);
            }
        }

        foreach (var seed in seeds)
        {
            AddUnique(seed);

            if (seed.ChunkOrder < 0)
            {
                continue;
            }

            var from = seed.ChunkOrder + 1;
            var to = seed.ChunkOrder + neighborDistance;

            var neighborQuery = new
            {
                size = neighborDistance,
                query = new
                {
                    @bool = new
                    {
                        filter = new object[]
                        {
                            new
                            {
                                @bool = new
                                {
                                    should = new object[]
                                    {
                                        new { term = new { documentId = seed.DocumentId } },
                                        new { term = new Dictionary<string, object> { ["DocumentId.keyword"] = seed.DocumentId } },
                                        new { term = new Dictionary<string, object> { ["DocumentId"] = seed.DocumentId } }
                                    },
                                    minimum_should_match = 1
                                }
                            },
                            new
                            {
                                @bool = new
                                {
                                    should = new object[]
                                    {
                                        new { range = new { chunkOrder = new { gte = from, lte = to } } },
                                        new { range = new Dictionary<string, object> { ["ChunkOrder"] = new { gte = from, lte = to } } }
                                    },
                                    minimum_should_match = 1
                                }
                            }
                        }
                    }
                }
            };

            using var response = await PostJsonAsync($"/{themedChunksIndex}/_search", neighborQuery, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("hits", out var hitsRoot)
                || !hitsRoot.TryGetProperty("hits", out var hits))
            {
                continue;
            }

            var neighbors = new List<RetrievedChunk>();
            foreach (var neighborHit in hits.EnumerateArray())
            {
                var source = neighborHit.GetProperty("_source");
                var score = neighborHit.TryGetProperty("_score", out var scoreElement) ? scoreElement.GetDouble() : 0d;
                var chunkOrder = TryGetInt32Property(source, out var parsedChunkOrder, "chunkOrder", "ChunkOrder")
                    ? parsedChunkOrder
                    : -1;

                neighbors.Add(new RetrievedChunk(
                    ChunkId: GetStringProperty(source, "chunkId", "ChunkId"),
                    ChunkOrder: chunkOrder,
                    DocumentId: GetStringProperty(source, "documentId", "DocumentId"),
                    Theme: GetStringProperty(source, "theme", "Theme"),
                    Content: GetStringProperty(source, "content", "Content"),
                    SectionPath: GetStringProperty(source, "sectionPath", "SectionPath"),
                    FileName: GetStringProperty(source, "fileName", "FileName"),
                    SourceUrl: GetNullableStringProperty(source, "sourceUrl", "SourceUrl"),
                    Score: score));
            }

            foreach (var neighbor in neighbors.OrderBy(n => n.ChunkOrder))
            {
                AddUnique(neighbor);
            }
        }

        return result.Count > 0 ? result : seeds.ToArray();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var baseUrl = (_options.Url ?? "http://localhost:9200").TrimEnd('/');
        return new HttpRequestMessage(method, $"{baseUrl}{relativeUrl}");
    }

    private string GetThemedDocumentsIndex(string theme)
    {
        var normalizedTheme = NormalizeThemeForIndex(theme);
        return $"{_options.DocumentsIndex}-{normalizedTheme}";
    }

    private string GetThemedChunksIndex(string theme)
    {
        var normalizedTheme = NormalizeThemeForIndex(theme);
        return $"{_options.ChunksIndex}-{normalizedTheme}";
    }

    private static string NormalizeThemeForIndex(string theme)
    {
        var source = (theme ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("Theme e obrigatorio.");
        }

        var builder = new StringBuilder(source.Length);
        var lastWasHyphen = false;

        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasHyphen = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.' || ch == '/')
            {
                if (!lastWasHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                    lastWasHyphen = true;
                }
            }
        }

        var normalized = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Theme invalido para composicao de indice.");
        }

        return normalized;
    }

    private static string GetStringProperty(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string? GetNullableStringProperty(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int GetInt32Property(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return 0;
    }

    private static bool TryGetInt32Property(JsonElement source, out int value, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var element))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
                {
                    value = number;
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static DateTime GetDateTimeProperty(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (source.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var parsedDate))
                {
                    return parsedDate;
                }

                if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return DateTime.MinValue;
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativeUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient(nameof(RagElasticsearchRepository));
        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> PutJsonAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, relativeUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient(nameof(RagElasticsearchRepository));
        return await client.SendAsync(request, cancellationToken);
    }
}
