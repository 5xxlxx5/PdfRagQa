namespace PdfRagQa.Domain.Abstractions;

/// <summary>文档分类端口：基于 PDF 预览内容判定文档类型（使用手册/宣传手册）。</summary>
public interface IDocumentClassifier
{
    Task<DocumentType> ClassifyAsync(ICollection<PdfPageText> preview, CancellationToken ct = default);
}