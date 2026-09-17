namespace PdfRagQa.Domain.Abstractions;

/// <summary>引用构造端口：把检索命中的 chunk 组装成结构化 Citation（page_no + bbox + chunk_id）。</summary>
public interface ICitationBuilder
{
    IReadOnlyList<Citation> BuildCitations(IReadOnlyList<DocumentChunk> chunks, IEnumerable<int> usedIndexes);
}