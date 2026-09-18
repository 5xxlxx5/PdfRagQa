namespace PdfRagQa.Infrastructure.Retrieval;

/// <summary>
/// 查询分词：把自然语言查询切成检索词。
///
/// 中文没有空格，按空白切词会得到 0 个词——这正是原先关键词检索恒为 0 分的原因。
/// 这里采用字符二元组（bigram）方案：
///   接线方法   → 接线 / 线方 / 方法（短词额外保留整体，提升精确匹配）
/// 英文与编号按连续字母数字整体保留：
///   E-203 怎么处理 → E-203 / 怎么 / 么处 / 处理
///
/// 选 bigram 而非词典分词的原因：工业手册里的型号与故障码靠通用词典切不出来，
/// bigram 不依赖词典、对未登录词天然友好；代价是词表变大，用 BM25 的 IDF 加权即可抑制噪声。
/// </summary>
public static class QueryTokenizer
{
    /// <summary>检索词数量上限，避免长查询生成过多 SQL 参数。</summary>
    public const int MaxTerms = 16;

    public static IReadOnlyList<string> Tokenize(string query)
    {
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(query)) return terms;

        var i = 0;
        while (i < query.Length && terms.Count < MaxTerms)
        {
            var c = query[i];

            if (IsCjk(c))
            {
                var start = i;
                while (i < query.Length && IsCjk(query[i])) i++;
                var run = query[start..i];

                if (run.Length == 1)
                {
                    terms.Add(run);
                }
                else
                {
                    for (var k = 0; k + 1 < run.Length && terms.Count < MaxTerms; k++)
                        terms.Add(run.Substring(k, 2));

                    // 短词额外保留整体，让「接线方法」这类完整表述也能精确命中
                    if (run.Length <= 4 && terms.Count < MaxTerms)
                        terms.Add(run);
                }
            }
            else if (char.IsLetterOrDigit(c))
            {
                var start = i;
                while (i < query.Length &&
                       (char.IsLetterOrDigit(query[i]) || query[i] is '-' or '_' or '.' or '/'))
                {
                    i++;
                }

                var token = query[start..i].Trim('-', '_', '.', '/');
                if (token.Length >= 2) terms.Add(token);
            }
            else
            {
                i++;
            }
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCjk(char c) =>
        (c >= '\u3400' && c <= '\u4DBF') ||   // CJK 扩展 A
        (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK 基本区
        (c >= '\uF900' && c <= '\uFAFF');     // CJK 兼容区
}
