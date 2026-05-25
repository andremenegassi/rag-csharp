namespace IA.API.Infrastructure.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ChatModel { get; set; } = "gpt-5-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-large";
}

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Url { get; set; } = "http://localhost:9200";
    public string IndexPrefix { get; set; } = "rag";
    public string DocumentsIndex { get; set; } = "rag-documents";
    public string ChunksIndex { get; set; } = "rag-chunks";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public int ChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 200;
    public int MaxChunkUtf8Bytes { get; set; } = 128 * 1024;
    public int MaxBulkRequestBytes { get; set; } = 4 * 1024 * 1024;
    public int NeighborDistance { get; set; } = 4;
    public int DefaultTopK { get; set; } = 5;
    public int MaxTopK { get; set; } = 20;
    public int MinTopK { get; set; } = 1;
    public int ContextTokenLimit { get; set; } = 6000;
    public string EmbeddingFieldName { get; set; } = "embedding";
    public int EmbeddingBatchSize { get; set; } = 32;
    public int EmbeddingMaxConcurrency { get; set; } = 4;
}

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public int MaxFileSizeMb { get; set; } = 10;
    public string[] AllowedExtensions { get; set; } = [".md", ".pdf"];
}

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public bool Enabled { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 15;
}
