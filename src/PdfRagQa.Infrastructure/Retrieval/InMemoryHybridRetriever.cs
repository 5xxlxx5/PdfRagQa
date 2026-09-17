using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// 混合检索（关键词 + 向量）实现，针对使用手册。
/// 骨架：在内存检索 + 简单重排序，后续接入真实 embedding/rerank 服务。
/// </summary>
public sealed class InMemoryHybridRetriever(IVectorStore store) : IHybridRetriever
{
    public async Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(HybridQuery query, CancellationToken ct = default)
    {
        var keyword = await store.KeywordSearchAsync(query.Query, query.TopK, query.DocumentId, query.Version, query.Language, ct);
        // 注：向量路与重排序在接入真实 IVectorStore/IEmbeddingProvider 后补充
        return keyword.Take(query.TopK).ToList();
    }
}