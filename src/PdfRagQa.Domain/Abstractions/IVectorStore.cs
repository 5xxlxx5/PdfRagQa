using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Domain.Abstractions;

/// <summary>文本/向量存储端口：写入/查询按文档、版本、语言过滤。</summary>
public interface IVectorStore
{
    Task UpsertChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentChunk>> KeywordSearchAsync(
        string query, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentChunk>> VectorSearchAsync(
        float[] queryVector, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default);

    Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken ct = default);
}