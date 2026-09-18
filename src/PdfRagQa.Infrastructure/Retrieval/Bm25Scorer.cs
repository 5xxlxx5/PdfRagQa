namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>语料统计：BM25 计分所需的全库信息。</summary>
/// <param name="DocumentCount">参与检索的 chunk 总数（N）。</param>
/// <param name="AverageLength">chunk 平均字符长度（avgdl）。</param>
/// <param name="DocumentFrequency">每个检索词命中的 chunk 数（df）。</param>
public sealed record CorpusStats(
    int DocumentCount,
    double AverageLength,
    IReadOnlyDictionary<string, int> DocumentFrequency);

/// <summary>
/// BM25 计分（k1 = 1.2，b = 0.75，业界默认取值）。
///
/// 相比「数一数词出现几次」，BM25 多了两项关键修正：
///   IDF      —— 常见词（"方法"、"进行"）权重低，罕见词（"E-203"）权重高；
///   长度归一 —— 长文本天然更容易命中，必须惩罚，否则长 chunk 会霸榜。
/// </summary>
public static class Bm25Scorer
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    public static double Score(string text, IReadOnlyList<string> terms, CorpusStats stats)
    {
        if (string.IsNullOrEmpty(text) || stats.DocumentCount <= 0 || stats.AverageLength <= 0)
            return 0;

        var length = text.Length;
        var score = 0.0;

        foreach (var term in terms)
        {
            var tf = CountOccurrences(text, term);
            if (tf == 0) continue;

            if (!stats.DocumentFrequency.TryGetValue(term, out var df) || df <= 0) continue;

            // +1 平滑，避免 df 接近 N 时 IDF 变成负数
            var idf = Math.Log((stats.DocumentCount - df + 0.5) / (df + 0.5) + 1.0);

            var norm = tf + K1 * (1 - B + B * length / stats.AverageLength);
            score += idf * tf * (K1 + 1) / norm;
        }

        return score;
    }

    /// <summary>
    /// 不重叠地统计出现次数。重叠匹配会重复计分
    /// （例如 "aaaa" 里按重叠能数出 3 个 "aa"，实际只有 2 个），故每命中一次就跳过整个词长。
    /// </summary>
    private static int CountOccurrences(string text, string term)
    {
        if (term.Length == 0 || text.Length < term.Length) return 0;

        var count = 0;
        var index = 0;
        while (index <= text.Length - term.Length)
        {
            var found = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;

            count++;
            index = found + term.Length;
        }

        return count;
    }
}
