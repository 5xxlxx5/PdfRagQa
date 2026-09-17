namespace PdfRagQa.Domain.Abstractions;

/// <summary>多模态检索（文本 + 视觉）用例，针对宣传手册。</summary>
public sealed class MultimodalQuery
{
    public string Query { get; init; } = string.Empty;
    public string? Version { get; init; }
    public LanguageCode? Language { get; init; }
    public string? DocumentId { get; init; }
    public int TopK { get; init; } = 20;
}

public interface IMultimodalRetriever
{
    Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(MultimodalQuery query, CancellationToken ct = default);
}