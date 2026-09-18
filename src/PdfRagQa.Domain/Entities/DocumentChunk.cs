namespace PdfRagQa.Domain;

/// <summary>
/// 检索/索引的最小单元。使用手册为段落/表格；宣传手册为版面图文区块。
/// 引用即指向此 chunk，携带 page_no + bbox 支持前端高亮。
/// </summary>
public sealed class DocumentChunk
{
    public string ChunkId { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// 所属文档标题。为引用展示而做的反规范化——检索时由 chunk 表关联 document 表一并取出，
    /// 避免为每条引用再查一次文档元数据（需求文档 FR6 要求引用包含来源文档）。
    /// </summary>
    public string DocumentTitle { get; init; } = string.Empty;

    public int PageNo { get; init; }
    public BoundingBox? Bbox { get; init; }
    public string Section { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
    public string? TableMarkdown { get; init; }

    /// <summary>宣传手册中关联的图片描述/引用。</summary>
    public string? ImageRef { get; init; }
    public string? ImageDescription { get; init; }

    /// <summary>生成该块向量所用的 embedding 模型与维度（模型切换需重建索引）。</summary>
    public string EmbeddingModel { get; init; } = string.Empty;
    public int Dimension { get; init; }
}