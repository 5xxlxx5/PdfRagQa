namespace PdfRagQa.Domain.Abstractions;

/// <summary>混合检索（关键词 + 向量）用例，针对使用手册。</summary>
public sealed class HybridQuery
{
    public string Query { get; init; } = string.Empty;

    /// <summary>为空时默认检索最新版；可指定具体版本。</summary>
    public string? Version { get; init; }
    public LanguageCode? Language { get; init; }
    public string? DocumentId { get; init; }

    public int TopK { get; init; } = 20;
}

public interface IHybridRetriever
{
    Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(HybridQuery query, CancellationToken ct = default);
}