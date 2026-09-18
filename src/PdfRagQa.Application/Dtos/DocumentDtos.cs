using PdfRagQa.Domain;

namespace PdfRagQa.Application.Dtos;

/// <summary>文档接入请求。</summary>
public sealed class ImportDocumentRequest
{
    public string FilePath { get; init; } = string.Empty;
    public string? Version { get; init; }

    /// <summary>
    /// 管理员声明的文档类型，决定走哪条解析链路：
    ///   Manual（使用手册）  → 文本抽取，保留精确原文与词项坐标；
    ///   Brochure（宣传手册）→ 页面渲染 + 视觉模型识别。
    /// 未声明时暂按使用手册处理并返回告警。
    /// </summary>
    public DocumentType? DocumentType { get; init; }

    public LanguageCode? Language { get; init; }
}

/// <summary>文档接入结果。</summary>
public sealed record ImportDocumentResult(
    string DocumentId,
    string Version,
    int ChunkCount,
    DocumentType DocumentType,
    DocumentTypeSource TypeSource,
    IReadOnlyList<string> ParseNotes,
    IReadOnlyList<string> Warnings);
