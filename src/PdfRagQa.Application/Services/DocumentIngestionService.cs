using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Application.Services;

/// <summary>
/// 文档接入用例（FR1 预处理 + FR2 分类 + 版本/语言登记）。
/// 骨架：完成 PDF 预览 -> 分类 -> 入仓，切片/向量由后续实现补齐。
/// </summary>
public sealed class DocumentIngestionService(
    IPdfParser pdfParser,
    IDocumentClassifier classifier,
    IDocumentRepository repository)
{
    public async Task<ImportDocumentResult> ImportAsync(
        ImportDocumentRequest request, Func<string, int, BoundingBox?> layout, CancellationToken ct = default)
    {
        var preview = await pdfParser.ExtractPreviewAsync(request.FilePath, ct);
        var type = request.DocumentType ?? await classifier.ClassifyAsync(preview, ct);
        var docId = Path.GetFileNameWithoutExtension(request.FilePath);
        var version = request.Version ?? "1.0";

        var doc = new Document
        {
            DocumentId = docId,
            Version = version,
            Title = docId,
            Type = type,
            Language = request.Language ?? LanguageCode.Zh,
            SourceFile = request.FilePath,
            IsLatest = await repository.GetLatestAsync(docId, ct) is null,
            ImportedAt = DateTimeOffset.UtcNow,
        };
        await repository.UpsertAsync(doc, ct);

        // 占位：后续实现切分 + embedding + 写 IVectorStore + build citations
        return new ImportDocumentResult(doc.DocumentId, doc.Version, ChunkCount: 0);
    }
}