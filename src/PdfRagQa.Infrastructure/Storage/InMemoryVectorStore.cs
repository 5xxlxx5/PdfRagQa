using System.Collections.Concurrent;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Storage;

/// <summary>
/// 内存版文本/向量存储（骨架占位，便于本地运行）。
/// 生产应替换为 Milvus / Qdrant / FAISS 封装。
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, (DocumentChunk Chunk, float[] Vector)> _items = new();

    public Task UpsertChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct = default)
    {
        _items[chunk.ChunkId] = (chunk, vector);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentChunk>> KeywordSearchAsync(
        string query, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scored = _items.Values
            .Where(Matches(documentId, version, language))
            .Select(e => (e.Chunk, Score: e.Chunk.Text.Count(c => words.Contains(c.ToString()))
                + (e.Chunk.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3 : 0)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(scored);
    }

    public Task<IReadOnlyList<DocumentChunk>> VectorSearchAsync(
        float[] queryVector, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
    {
        var results = _items.Values
            .Where(Matches(documentId, version, language))
            .Select(e => (e.Chunk, Score: Cosine(e.Vector, queryVector)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentChunk>>(results);
    }

    public Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(chunkId, out var item) ? item.Chunk : null);

    private static Func<(DocumentChunk Chunk, float[] Vector), bool> Matches(
        string? documentId, string? version, LanguageCode? language)
        => kv => (documentId is null || kv.Chunk.DocumentId == documentId)
               && (version is null || kv.Chunk.Version == version);
    // 注：language 过滤待 DocumentChunk 补充语言字段后实现

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0f : dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
    }
}