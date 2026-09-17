namespace PdfRagQa.Domain.Abstractions;

/// <summary>重排序端口：对召回结果精排，提升顶部相关性。</summary>
public interface IReranker
{
    Task<IReadOnlyList<RerankScore>> RerankAsync(string query, ICollection<DocumentChunk> candidates, CancellationToken ct = default);
}

/// <summary>重排序得分（chunkId -> 得分）。</summary>
public sealed record RerankScore(string ChunkId, float Score);