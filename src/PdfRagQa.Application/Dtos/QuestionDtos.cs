using PdfRagQa.Domain;

namespace PdfRagQa.Application.Dtos;

/// <summary>对外问答请求（按需求文档 FR2/FR3/FR8）。</summary>
public sealed class QuestionRequest
{
    public string Question { get; init; } = string.Empty;

    /// <summary>多轮对话会话 ID，首轮可为空。</summary>
    public string? ConversationId { get; init; }

    /// <summary>可为空（自动识别），也可显式指定文档类型。</summary>
    public DocumentType? DocumentType { get; init; }

    /// <summary>指定版本（默认最新版）。</summary>
    public string? Version { get; init; }

    public LanguageCode? Language { get; init; }
    public string? DocumentId { get; init; }
}

/// <summary>对外问答响应（answer + citations + evidence）。</summary>
public sealed class QuestionResponse
{
    public string ConversationId { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;

    /// <summary>溯源引用（page_no + bbox + chunk_id）。</summary>
    public IReadOnlyList<CitationDto> Citations { get; init; } = [];

    /// <summary>多模态证据（宣传手册中的图片）。</summary>
    public IReadOnlyList<string> ImageRefs { get; init; } = [];

    public float Confidence { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
}

public sealed class CitationDto
{
    public string DocumentId { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ChunkId { get; init; } = string.Empty;
    public int PageNo { get; init; }
    public string Section { get; init; } = string.Empty;
    public BoundingBoxDto? Bbox { get; init; }
}

public sealed class BoundingBoxDto(double x, double y, double w, double h)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = w;
    public double Height { get; } = h;
}

/// <summary>
/// 检索预览请求：只跑检索、不调用生成模型。
/// 用于调参与排障——引用只包含模型标注的片段，没有生成模型时无法从问答响应观察检索结果。
/// </summary>
public sealed class RetrievalPreviewRequest
{
    public string Query { get; init; } = string.Empty;
    public string? DocumentId { get; init; }

    /// <summary>留空则按需求文档 FR3 默认限定到最新版。</summary>
    public string? Version { get; init; }

    public LanguageCode? Language { get; init; }
    public int TopK { get; init; } = 10;
}

/// <summary>检索预览结果项。<paramref name="TextPreview"/> 截断展示，避免响应过大。</summary>
public sealed record RetrievalPreviewItem(
    string ChunkId,
    string DocumentId,
    string DocumentTitle,
    string Version,
    int PageNo,
    string Section,
    string TextPreview);

public sealed record RetrievalPreviewResponse(
    string Query,
    int Count,
    IReadOnlyList<RetrievalPreviewItem> Chunks);