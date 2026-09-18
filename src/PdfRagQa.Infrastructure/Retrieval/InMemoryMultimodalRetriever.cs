using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// 多模态检索实现，针对宣传手册（图文区块）。
/// 复用混合检索思路：关键词 BM25 + 向量余弦双路召回，RRF 融合。
///
/// 为什么需要向量路：宣传册文本短、图多，纯关键词对「pm-k45 是什么产品」这类语义问法
/// 常常 0 命中——检索词与正文没有一个对得上。而接入阶段已为每个块写入 1024 维向量，
/// 用它对问题做向量检索即可用语义召回；向量路失败自动退化为关键词单路。
/// </summary>
public sealed class InMemoryMultimodalRetriever(
    IVectorStore store,
    IEmbeddingProvider embeddingProvider) : IMultimodalRetriever
{
    /// <summary>每路召回的候选数。取 topK 的若干倍，给融合留出挑选空间。</summary>
    private const int PerRouteMultiplier = 3;

    public async Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(
        MultimodalQuery query, CancellationToken ct = default)
    {
        if (query.TopK <= 0) return Array.Empty<DocumentChunk>();

        var perRoute = Math.Max(query.TopK * PerRouteMultiplier, query.TopK);

        var keywordTask = store.KeywordSearchAsync(
            query.Query, perRoute, query.DocumentId, query.Version, query.Language, ct);

        var vectorTask = SearchByVectorAsync(query, perRoute, ct);

        await Task.WhenAll(keywordTask, vectorTask).ConfigureAwait(false);

        var fused = RrfFusion.Fuse(
            [await keywordTask.ConfigureAwait(false), await vectorTask.ConfigureAwait(false)],
            query.TopK);

        return fused.Count > 0 ? fused : await keywordTask.ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DocumentChunk>> SearchByVectorAsync(
        MultimodalQuery query, int topK, CancellationToken ct)
    {
        try
        {
            var vectors = await embeddingProvider.EmbedTextsAsync([query.Query], ct).ConfigureAwait(false);
            if (vectors.Count == 0) return Array.Empty<DocumentChunk>();

            return await store.VectorSearchAsync(
                vectors[0], topK, query.DocumentId, query.Version, query.Language, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 向量路失败不阻断检索：关键词路仍然可用
            return Array.Empty<DocumentChunk>();
        }
    }
}