namespace PdfRagQa.Domain.Abstractions;

/// <summary>渲染出的单页图像（PNG 字节），供视觉模型识别。</summary>
public sealed record PdfPageImage(
    int PageNo,
    byte[] PngBytes,
    int PixelWidth,
    int PixelHeight);

/// <summary>
/// PDF 页面栅格化端口：把页面渲染成图片。
/// 这是宣传手册链路的前置步骤——视觉模型看的是像素，因此不受 PDF 文字层编码问题影响。
/// </summary>
public interface IPdfPageRenderer
{
    /// <summary>
    /// 逐页渲染，以流式方式产出，避免一次性把所有页面的位图读进内存。
    /// </summary>
    IAsyncEnumerable<PdfPageImage> RenderAsync(string filePath, CancellationToken ct = default);
}
