using System.Collections.Concurrent;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Storage;

/// <summary>内存版文档元数据仓库（骨架占位）。</summary>
public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<string, Document> _items = new();

    public Task UpsertAsync(Document document, CancellationToken ct = default)
    {
        _items[$"{document.DocumentId}|{document.Version}"] = document;
        return Task.CompletedTask;
    }

    public Task<Document?> GetAsync(string documentId, string? version = null, CancellationToken ct = default)
    {
        if (version is not null)
            return Task.FromResult(_items.TryGetValue($"{documentId}|{version}", out var d) ? d : null);
        return GetLatestAsync(documentId, ct);
    }

    public Task<Document?> GetLatestAsync(string documentId, CancellationToken ct = default)
    {
        var latest = _items.Values
            .Where(d => d.DocumentId == documentId)
            .OrderByDescending(d => d.ImportedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<IReadOnlyList<Document>> QueryAsync(
        string? documentId = null, string? version = null, DocumentType? type = null, CancellationToken ct = default)
    {
        var result = _items.Values
            .Where(d => documentId is null || d.DocumentId == documentId)
            .Where(d => version is null || d.Version == version)
            .Where(d => type is null || d.Type == type)
            .ToList();
        return Task.FromResult<IReadOnlyList<Document>>(result);
    }
}