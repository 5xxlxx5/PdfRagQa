namespace PdfRagQa.Domain;

/// <summary>页面边界框坐标，用于答案引用在原文 PDF 中高亮定位。</summary>
public readonly record struct BoundingBox(double X, double Y, double Width, double Height);