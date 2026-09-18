namespace PdfRagQa.Domain;

/// <summary>文档接入后的实体：一份文档可存在多个版本（document_id 相同，version 不同）。</summary>
public sealed class Document
{
    public string DocumentId { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public string Title { get; init; } = string.Empty;

    /// <summary>实际用于路由解析链路的文档类型（可能因文字层不可用而相对声明值降级）。</summary>
    public DocumentType Type { get; init; }

    /// <summary>该类型的来源：管理员声明还是自动判定。</summary>
    public DocumentTypeSource TypeSource { get; init; } = DocumentTypeSource.Auto;

    /// <summary>管理员声明的类型；未声明时为 null。</summary>
    public DocumentType? DeclaredType { get; init; }

    /// <summary>自动判定的意见，始终记录，用于与人工声明做准确率对照。</summary>
    public DocumentType? AutoType { get; init; }

    /// <summary>判定依据与冲突告警（分号分隔），用于事后审计。</summary>
    public string? TypeReasons { get; init; }

    public LanguageCode Language { get; init; } = LanguageCode.Zh;
    public string SourceFile { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>是否为该文档最新版本（检索默认命中最新版）。</summary>
    public bool IsLatest { get; init; }
}
