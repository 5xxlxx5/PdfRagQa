using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure;

/// <summary>
/// 把模型实际引用的 chunk 组装为结构化 Citation（page_no + bbox + chunk_id），
/// 供前端在原文 PDF 中高亮定位（需求文档 FR6）。
///
/// 只返回「模型标注引用了的」片段。usedIndexes 为空表示模型没有标注任何依据，
/// 此时返回空集而不是退回「全部上下文」——后者会让引用看起来覆盖了所有召回内容，
/// 并把置信度恒定为 1，恰好掩盖了「答案其实没有依据」这个最需要暴露的问题。
/// </summary>
public sealed class CitationBuilder : ICitationBuilder
{
    public IReadOnlyList<Citation> BuildCitations(
        IReadOnlyList<DocumentChunk> chunks, IEnumerable<int> usedIndexes)
    {
        if (chunks.Count == 0 || usedIndexes is null) return Array.Empty<Citation>();

        return usedIndexes
            .Where(index => index >= 0 && index < chunks.Count)
            .Distinct()
            .OrderBy(index => index)
            .Select(index => ToCitation(chunks[index]))
            .ToList();
    }

    private static Citation ToCitation(DocumentChunk chunk) => new()
    {
        DocumentId = chunk.DocumentId,
        DocumentTitle = chunk.DocumentTitle,
        Version = chunk.Version,
        ChunkId = chunk.ChunkId,
        PageNo = chunk.PageNo,
        Bbox = chunk.Bbox,
        Section = chunk.Section,
    };
}
