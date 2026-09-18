namespace PdfRagQa.Domain.Abstractions;

/// <summary>PDF 单个词项（含坐标，用于答案溯源时在原文中高亮定位）。</summary>
public sealed record PdfWord(string Text, double X, double Y, double Width, double Height);

/// <summary>PDF 单页文本：整页文本 + 词项坐标 + 页面尺寸。</summary>
public sealed record PdfTextPage(
    int PageNo,
    string Text,
    IReadOnlyList<PdfWord> Words,
    double Width,
    double Height);

/// <summary>
/// 使用手册链路的文本抽取端口。
/// 产出精确原文与词项坐标，供后续切片、混合检索与 bbox 溯源使用。
/// </summary>
public interface IPdfTextExtractor
{
    Task<IReadOnlyList<PdfTextPage>> ExtractAsync(string filePath, CancellationToken ct = default);
}
