using PdfRagQa.Domain.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfRagQa.Infrastructure.Pdf;

/// <summary>
/// 使用手册链路的文本抽取（PdfPig）：
/// 逐页取出整页文本、词项坐标与页面尺寸。词项坐标用于后续切片与答案溯源高亮。
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<IReadOnlyList<PdfTextPage>> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        var pages = new List<PdfTextPage>();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add(new PdfTextPage(
                PageNo: page.Number,
                Text: ExtractPageText(page),
                Words: ExtractPageWords(page),
                Width: page.Width,
                Height: page.Height));
        }

        return Task.FromResult<IReadOnlyList<PdfTextPage>>(pages);
    }

    /// <summary>优先 PdfPig 内置整页文本；为空时退化为按词项重组。</summary>
    private static string ExtractPageText(Page page)
    {
        var text = Normalize(page.Text);
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        var words = ExtractPageWords(page);
        return words.Count == 0 ? string.Empty : string.Join(" ", words.Select(w => w.Text));
    }

    private static IReadOnlyList<PdfWord> ExtractPageWords(Page page)
    {
        try
        {
            return page.GetWords()
                .Select(w => new PdfWord(
                    w.Text,
                    w.BoundingBox.Left,
                    w.BoundingBox.Bottom,
                    w.BoundingBox.Width,
                    w.BoundingBox.Height))
                .ToList();
        }
        catch
        {
            // 个别页面的词项抽取可能因字体问题失败，不影响整份文档
            return Array.Empty<PdfWord>();
        }
    }

    private static string Normalize(string text)
        => string.Join("\n", text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()))
            .Trim();
}
