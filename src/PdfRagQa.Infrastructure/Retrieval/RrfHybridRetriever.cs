using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// 混合检索实现（需求文档 FR4）：关键词 BM25 与向量余弦双路召回，用 RRF 融合。
///
/// 两路各自的价值：关键词擅长型号、故障码、参数这类精确串（"E-203"）；
/// 向量擅长同义改写（"怎么接线" 能召回 "配线步骤"）。缺任何一路都会在对应场景失准。
///
/// 向量路是「尽力而为」：查询向量化失败（未配置 embedding、服务不可用、超时）时
/// 自动退化为关键词单路，而不是让整个检索失败——关键词路独立可用，没有理由被拖垮。
/// </summary>
public sealed class RrfHybridRetriever(
    IVectorStore store,
    IEmbeddingProvider embeddingProvider) : IHybridRetriever
{
    /// <summary>每路召回的候选数。取 topK 的若干倍，给融合留出挑选空间。</summary>
    private const int PerRouteMultiplier = 3;

    public async Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(
        HybridQuery query, CancellationToken ct = default)
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

        // 两路都空时退回关键词路的结果（理论上为空，此处仅作兜底避免返回空集）
        return fused.Count > 0 ? fused : await keywordTask.ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DocumentChunk>> SearchByVectorAsync(
        HybridQuery query, int topK, CancellationToken ct)
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
