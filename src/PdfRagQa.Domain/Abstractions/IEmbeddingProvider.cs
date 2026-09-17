namespace PdfRagQa.Domain.Abstractions;

/// <summary>文本/视觉向量生成端口。</summary>
public interface IEmbeddingProvider
{
    string ModelName { get; }
    int Dimension { get; }

    Task<IReadOnlyList<float[]>> EmbedTextsAsync(IEnumerable<string> texts, CancellationToken ct = default);
    Task<float[]> EmbedImageAsync(Stream imageStream, CancellationToken ct = default);
}