using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure;

/// <summary>把命中的 chunk 组装为结构化 Citation（page_no + bbox + chunk_id）。</summary>
public sealed class CitationBuilder : ICitationBuilder
{
    public IReadOnlyList<Citation> BuildCitations(IReadOnlyList<DocumentChunk> chunks, IEnumerable<int> usedIndexes)
    {
        var used = usedIndexes.ToHashSet();
        return chunks
            .Select((chunk, i) => (chunk, i))
            .Where(x => used is null || used.Count == 0 || used.Contains(x.i))
            .Select(x => new Citation
            {
                DocumentId = x.chunk.DocumentId,
                DocumentTitle = x.chunk.Section,
                Version = x.chunk.Version,
                ChunkId = x.chunk.ChunkId,
                PageNo = x.chunk.PageNo,
                Bbox = x.chunk.Bbox,
                Section = x.chunk.Section,
            })
            .ToList();
    }
}