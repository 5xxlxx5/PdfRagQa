using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using PDFtoImage;
using PdfRagQa.Domain.Abstractions;
using SkiaSharp;

namespace PdfRagQa.Infrastructure.Pdf;

/// <summary>
/// 页面栅格化实现（PDFium via PDFtoImage）。
/// 宣传手册链路的前置步骤：把页面渲染成 PNG 交给视觉模型，
/// 因为视觉模型读的是像素，不受 PDF 文字层编码（如 GBK-EUC-H 预定义 CMap）影响。
///
/// PDFium 仅支持桌面 / 服务端平台，不支持浏览器，故在此显式标注支持范围。
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class PdfiumPageRenderer(AiOptions options) : IPdfPageRenderer
{
    public async IAsyncEnumerable<PdfPageImage> RenderAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 整份文件读一次，避免逐页重复读取
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        var pageCount = Conversion.GetPageCount(bytes);

        for (var index = 0; index < pageCount; index++)
        {
            ct.ThrowIfCancellationRequested();

            // 逐页渲染并立即编码，位图用完即释放，内存占用与页数无关
            using var bitmap = Conversion.ToImage(
                bytes,
                page: index,
                options: new RenderOptions(Dpi: options.RenderDpi));

            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);

            yield return new PdfPageImage(
                PageNo: index + 1,
                PngBytes: encoded.ToArray(),
                PixelWidth: bitmap.Width,
                PixelHeight: bitmap.Height);
        }
    }
}
