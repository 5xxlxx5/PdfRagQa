using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Pdf;

/// <summary>
/// 占位实现：真实场景应以 PdfPig / 版面分析服务实现。
/// 当前仅根据文本行数粗判：文本稠密 -> 使用手册；文本稀疏 -> 宣传手册。
/// </summary>
public sealed class HeuristicDocumentClassifier : IDocumentClassifier
{
    public Task<DocumentType> ClassifyAsync(ICollection<PdfPageText> preview, CancellationToken ct = default)
    {
        var totalChars = preview.Sum(p => p.Text.Length);
        var avgPerPage = preview.Count == 0 ? 0 : totalChars / (double)preview.Count;
        return Task.FromResult(avgPerPage > 80 ? DocumentType.Manual : DocumentType.Brochure);
    }
}