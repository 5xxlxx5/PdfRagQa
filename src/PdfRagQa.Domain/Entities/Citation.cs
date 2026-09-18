namespace PdfRagQa.Domain;

/// <summary>一条答案的结构化引用来源。</summary>
public sealed class Citation
{
    public string DocumentId { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ChunkId { get; init; } = string.Empty;
    public int PageNo { get; init; }
    public BoundingBox? Bbox { get; init; }
    public string Section { get; init; } = string.Empty;

    /// <summary>支撑该答案的原文片段（截断），供前端溯源展示。</summary>
    public string TextPreview { get; init; } = string.Empty;
}