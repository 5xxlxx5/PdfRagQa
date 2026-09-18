namespace PdfRagQa.Domain.Abstractions;

/// <summary>视觉识别请求：一页图像。</summary>
public sealed record VisionRequest(int PageNo, byte[] PngBytes);

/// <summary>
/// 视觉识别结果。
/// <paramref name="Content"/> 为模型对该页的忠实转录与视觉描述（Markdown），
/// 是宣传手册链路唯一的内容来源——因此模型被明确要求「看不清就写无法辨认，不要猜」。
/// </summary>
public sealed record VisionResult(
    int PageNo,
    bool Succeeded,
    string Content,
    string? Error);

/// <summary>
/// 视觉模型端口：把页面图像识别为文本与视觉描述（宣传手册链路）。
/// 与 ILlmClient 的区别：ILlmClient 只吃文本上下文，本端口吃图像。
/// </summary>
public interface IVisionExtractor
{
    Task<VisionResult> RecognizeAsync(VisionRequest request, CancellationToken ct = default);
}
