namespace PdfRagQa.Application.Dtos;

/// <summary>
/// 导入进度增量（服务向后台作业上报，供前端轮询展示）。
/// TotalItems 在已知总量时提供（手册按页、写入按块）；流式渲染的宣传册在结束前未知，记为 null。
/// </summary>
public sealed record ImportProgressUpdate(
    string Stage,
    int CurrentItem,
    int? TotalItems,
    string? Note = null);

/// <summary>导入作业状态（前端轮询 GET /api/documents/import/{id} 的返回体）。</summary>
public sealed record ImportStatusDto(
    string JobId,
    bool IsCompleted,
    bool IsFailed,
    string Stage,
    int CurrentItem,
    int? TotalItems,
    string? Error = null,
    ImportDocumentResult? Result = null);