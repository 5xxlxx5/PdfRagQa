namespace PdfRagQa.Domain;

/// <summary>文档接入后的实体：一份文档可存在多个版本（document_id 相同，version 不同）。</summary>
public sealed class Document
{
    public string DocumentId { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public string Title { get; init; } = string.Empty;
    public DocumentType Type { get; init; }
    public LanguageCode Language { get; init; } = LanguageCode.Zh;
    public string SourceFile { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>是否为该文档最新版本（检索默认命中最新版）。</summary>
    public bool IsLatest { get; init; }
}