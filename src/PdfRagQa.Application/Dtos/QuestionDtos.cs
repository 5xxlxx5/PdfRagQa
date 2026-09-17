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