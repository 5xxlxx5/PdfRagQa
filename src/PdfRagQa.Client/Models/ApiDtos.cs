namespace PdfRagQa.Client.Models;

/// <summary>文档类型：与后端 Domain.DocumentType 一致（0=Manual 1=Brochure）。</summary>
public enum DocumentType
{
    Manual = 0,
    Brochure = 1,
}

/// <summary>语言：与后端 LanguageCode 一致。</summary>
public enum LanguageCode
{
    Zh = 0,
    En = 1,
    ZhEn = 2,
}

/// <summary>文档接入请求（PascalCase，服务端大小写不敏感绑定）。</summary>
public sealed class ImportDocumentRequest
{
    public string FilePath { get; init; } = string.Empty;
    public string? Version { get; init; }
    public DocumentType? DocumentType { get; init; }
    public LanguageCode? Language { get; init; }
}

/// <summary>导入作业状态（POST/import 提交后，GET import/{id} 轮询）。</summary>
public sealed class ImportStatusDto
{
    public string JobId { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public bool IsFailed { get; init; }
    public string Stage { get; init; } = string.Empty;
    public int CurrentItem { get; init; }
    public int? TotalItems { get; init; }
    public string? Error { get; init; }
    public ImportDocumentResult? Result { get; init; }
}

public sealed class ImportDocumentResult
{
    public string DocumentId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int ChunkCount { get; init; }
    public int DocumentType { get; init; }
    public int TypeSource { get; init; }
    public List<string> ParseNotes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>问答请求。</summary>
public sealed class QuestionRequest
{
    public string Question { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
    public DocumentType? DocumentType { get; init; }
    public string? Version { get; init; }
    public LanguageCode? Language { get; init; }
    public string? DocumentId { get; init; }
}

/// <summary>问答响应（服务端默认输出 camelCase，用大小写不敏感反序列化）。</summary>
public sealed class QuestionResponse
{
    public string ConversationId { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public List<CitationDto> Citations { get; init; } = [];
    public List<string> ImageRefs { get; init; } = [];
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
    public string TextPreview { get; init; } = string.Empty;
}