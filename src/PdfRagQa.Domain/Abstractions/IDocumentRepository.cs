namespace PdfRagQa.Domain.Abstractions;

/// <summary>文档元数据仓库端口。</summary>
public interface IDocumentRepository
{
    Task UpsertAsync(Document document, CancellationToken ct = default);
    Task<Document?> GetAsync(string documentId, string? version = null, CancellationToken ct = default);
    Task<Document?> GetLatestAsync(string documentId, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> QueryAsync(
        string? documentId = null, string? version = null, DocumentType? type = null, CancellationToken ct = default);
}