using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Application.Services;

/// <summary>
/// 文档接入用例（FR1 预处理 + FR2 分类 + 版本/语言登记）。
///
/// 解析策略由管理员声明的文档类型决定，不做自动判定：
///   使用手册（Manual）  → IPdfTextExtractor：PdfPig 抽取精确原文与词项坐标；
///   宣传手册（Brochure）→ IPdfPageRenderer + IVisionExtractor：页面渲染成图后交给视觉模型识别。
///
/// 宣传手册之所以走视觉链路，一是它不受 PDF 文字层编码影响
/// （例如方正 GBK-EUC-H 预定义 CMap 会让文本抽取产出乱码），
/// 二是宣传册的信息主要承载在版面上（配图、卖点排布、型号矩阵），纯文本链路拿不到。
/// </summary>
public sealed class DocumentIngestionService(
    IPdfTextExtractor textExtractor,
    IPdfPageRenderer pageRenderer,
    IVisionExtractor visionExtractor,
    IVectorStore vectorStore,
    IDocumentRepository repository)
{
    public async Task<ImportDocumentResult> ImportAsync(
        ImportDocumentRequest request, CancellationToken ct = default)
    {
        var declared = request.DocumentType;
        var type = declared ?? DocumentType.Manual;
        var source = declared is null ? DocumentTypeSource.Auto : DocumentTypeSource.Declared;

        var warnings = new List<string>();
        if (declared is null)
        {
            warnings.Add("未声明文档类型，当前按使用手册（文本链路）处理；建议在请求中显式指定 documentType。");
        }

        var docId = Path.GetFileNameWithoutExtension(request.FilePath);
        var version = request.Version ?? "1.0";
        var notes = new List<string>();

        var chunkCount = type == DocumentType.Manual
            ? await ParseManualAsync(docId, version, request.FilePath, notes, warnings, ct)
            : await ParseBrochureAsync(docId, version, request.FilePath, notes, warnings, ct);

        var doc = new Document
        {
            DocumentId = docId,
            Version = version,
            Title = docId,
            Type = type,
            TypeSource = source,
            DeclaredType = declared,
            TypeReasons = string.Join("; ", notes),
            Language = request.Language ?? LanguageCode.Zh,
            SourceFile = request.FilePath,
            ImportedAt = DateTimeOffset.UtcNow,
            // IsLatest 由仓储在写入时统一维护（先清掉同文档其他版本的标记，再把本次版本标为最新）。
            // 调用方不需要也不应该自己计算——原先在这里按「是否首次导入」判断，
            // 导致新版本导入后旧版仍被标为最新，检索默认限定最新版时会返回最旧的内容。
        };
        await repository.UpsertAsync(doc, ct);

        return new ImportDocumentResult(
            DocumentId: doc.DocumentId,
            Version: doc.Version,
            ChunkCount: chunkCount,
            DocumentType: type,
            TypeSource: source,
            ParseNotes: notes,
            Warnings: warnings);
    }

    /// <summary>使用手册链路：文本抽取，按页写入文本块，返回写入数量。</summary>
    private async Task<int> ParseManualAsync(
        string documentId,
        string version,
        string filePath,
        List<string> notes,
        List<string> warnings,
        CancellationToken ct)
    {
        var pages = await textExtractor.ExtractAsync(filePath, ct);
        var totalChars = pages.Sum(p => p.Text.Length);
        var totalWords = pages.Sum(p => p.Words.Count);
        notes.Add($"文本链路：{pages.Count} 页，{totalChars} 字符，{totalWords} 个词项");

        if (totalChars == 0)
        {
            warnings.Add(
                "文本抽取结果为空：该 PDF 可能没有文字层（扫描件），或文字编码不受支持。"
                + "若确认是图片型文档，请把 documentType 改为宣传手册走视觉链路。");
        }

        var written = 0;
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text)) continue;

            await vectorStore.UpsertChunkAsync(
                new DocumentChunk
                {
                    ChunkId = BuildChunkId(documentId, version, page.PageNo),
                    DocumentId = documentId,
                    Version = version,
                    PageNo = page.PageNo,
                    // 页级 bbox：整页范围，后续做段落级切片时会细化为词项并集
                    Bbox = new BoundingBox(0, 0, page.Width, page.Height),
                    Section = string.Empty,
                    Text = page.Text,
                },
                Array.Empty<float>(),
                ct);
            written++;
        }

        notes.Add($"已写入 {written} 个文本块（按页）");
        return written;
    }

    /// <summary>宣传手册链路：逐页渲染 + 视觉识别，按页写入内容块，返回写入数量。</summary>
    private async Task<int> ParseBrochureAsync(
        string documentId,
        string version,
        string filePath,
        List<string> notes,
        List<string> warnings,
        CancellationToken ct)
    {
        var rendered = 0;
        var recognized = 0;
        var written = 0;
        var errors = new List<string>();

        await foreach (var image in pageRenderer.RenderAsync(filePath, ct))
        {
            rendered++;

            var result = await visionExtractor.RecognizeAsync(
                new VisionRequest(image.PageNo, image.PngBytes), ct);

            if (!result.Succeeded)
            {
                if (errors.Count < 5) errors.Add($"第 {image.PageNo} 页：{result.Error}");
                continue;
            }

            recognized++;

            await vectorStore.UpsertChunkAsync(
                new DocumentChunk
                {
                    ChunkId = BuildChunkId(documentId, version, image.PageNo),
                    DocumentId = documentId,
                    Version = version,
                    PageNo = image.PageNo,
                    Bbox = new BoundingBox(0, 0, image.PixelWidth, image.PixelHeight),
                    Section = string.Empty,
                    Text = result.Content,
                },
                Array.Empty<float>(),
                ct);
            written++;
        }

        notes.Add($"视觉链路：渲染 {rendered} 页，识别成功 {recognized} 页，写入 {written} 个内容块");

        if (rendered == 0)
            warnings.Add("未能渲染出任何页面，请检查该 PDF 是否可正常打开。");
        else if (recognized == 0)
            warnings.Add("所有页面均未识别成功，请检查 Ai 配置与视觉模型服务是否可用。");

        if (errors.Count > 0)
            warnings.Add($"部分页面识别失败：{string.Join("；", errors)}");

        return written;
    }

    private static string BuildChunkId(string documentId, string version, int pageNo)
        => $"{documentId}:{version}:p{pageNo}";
}
