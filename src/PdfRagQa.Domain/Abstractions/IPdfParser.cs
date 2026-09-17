namespace PdfRagQa.Domain.Abstractions;

/// <summary>
/// PDF 解析端口：从 PDF 中抽取原始文本/版面结构（由基础设施层实现）。
/// </summary>
public interface IPdfParser
{
    /// <summary>解析 PDF，产出按页面组织的文本与版面块（用于分类决策前的最小解析）。</summary>
    Task<PdfPageText[]> ExtractPreviewAsync(string filePath, CancellationToken ct = default);
}

/// <summary>PDF 单页文本预览。</summary>
public sealed record PdfPageText(int PageNo, string Text);