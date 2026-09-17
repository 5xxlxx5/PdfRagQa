using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// 多模态检索实现，针对宣传手册（图文区块）。
/// 骨架：先复用关键词检索，后续接入视觉检索与图文 embedding。
/// </summary>
public sealed class InMemoryMultimodalRetriever(IVectorStore store) : IMultimodalRetriever
{
    public async Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(MultimodalQuery query, CancellationToken ct = default)
    {
        // 注：多模态 image-ref 参与检索待视觉索引就绪后实现
        return await store.KeywordSearchAsync(query.Query, query.TopK, query.DocumentId, query.Version, query.Language, ct);
    }
}