using PdfRagQa.Domain;

namespace PdfRagQa.Application.Dtos;

/// <summary>文档接入请求。</summary>
public sealed class ImportDocumentRequest
{
    public string FilePath { get; init; } = string.Empty;
    public string? Version { get; init; }
    public DocumentType? DocumentType { get; init; }
    public LanguageCode? Language { get; init; }
}

/// <summary>文档接入结果。</summary>
public sealed record ImportDocumentResult(string DocumentId, string Version, int ChunkCount);