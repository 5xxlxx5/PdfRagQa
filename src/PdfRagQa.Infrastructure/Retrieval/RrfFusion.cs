using PdfRagQa.Domain;

namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion：把多路召回结果按名次融合。
///
/// score(d) = Σ_r 1 / (k + rank_r(d))，k 取 60（原论文默认值）。
///
/// 用「名次」而不是「原始得分」的原因：BM25 得分与余弦相似度量纲完全不同，
/// 直接加权求和必须先归一化，而归一化又依赖各自的分数分布；
/// RRF 只关心各路内部的排序，天然免疫量纲问题，因此是混合检索的稳妥默认选择。
/// </summary>
public static class RrfFusion
{
    public const int DefaultK = 60;

    /// <summary>把多路已排序的召回结果融合成一路，按 RRF 得分降序取前 topK。</summary>
    public static IReadOnlyList<DocumentChunk> Fuse(
        IEnumerable<IReadOnlyList<DocumentChunk>> rankedLists,
        int topK,
        int k = DefaultK)
    {
        if (topK <= 0) return Array.Empty<DocumentChunk>();

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var chunks = new Dictionary<string, DocumentChunk>(StringComparer.Ordinal);

        foreach (var list in rankedLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var chunk = list[rank];
                if (string.IsNullOrEmpty(chunk.ChunkId)) continue;

                // 同一 chunk 可能被多路召回，得分累加——这正是 RRF 提升共同命中的方式
                chunks[chunk.ChunkId] = chunk;
                scores[chunk.ChunkId] = scores.GetValueOrDefault(chunk.ChunkId) + 1.0 / (k + rank + 1);
            }
        }

        return scores
            .OrderByDescending(pair => pair.Value)
            .Take(topK)
            .Select(pair => chunks[pair.Key])
            .ToList();
    }
}
