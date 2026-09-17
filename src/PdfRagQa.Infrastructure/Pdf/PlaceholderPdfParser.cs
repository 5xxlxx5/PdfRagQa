using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Pdf;

/// <summary>
/// 占位实现：真实场景应以 PdfPig 等库抽取文本。
/// 这里返回空文本，交由后续章节实现。
/// </summary>
public sealed class PlaceholderPdfParser : IPdfParser
{
    public Task<PdfPageText[]> ExtractPreviewAsync(string filePath, CancellationToken ct = default)
    {
        // TODO: 接入 PdfPig / 版面分析服务
        return Task.FromResult(Array.Empty<PdfPageText>());
    }
}