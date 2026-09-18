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
///
/// 解析结果统一经 IEmbeddingProvider 向量化后写入 chunk 表。
/// 向量化失败不阻断导入：先把文本存下来，向量可以后补，但要明确告警。
/// </summary>
public sealed class DocumentIngestionService(
    IPdfTextExtractor textExtractor,
    IPdfPageRenderer pageRenderer,
    IVisionExtractor visionExtractor,
    IEmbeddingProvider embeddingProvider,
    IVectorStore vectorStore,
    IDocumentRepository repository)
{
    public async Task<ImportDocumentResult> ImportAsync(
        ImportDocumentRequest request,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken ct = default)
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

        var chunks = type == DocumentType.Manual
            ? await ParseManualAsync(docId, version, request.FilePath, notes, warnings, progress, ct)
            : await ParseBrochureAsync(docId, version, request.FilePath, notes, warnings, progress, ct);

        var chunkCount = await WriteChunksAsync(chunks, notes, warnings, progress, ct);

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

    /// <summary>使用手册链路：文本抽取，按页产出一个文本块。</summary>
    private async Task<List<DocumentChunk>> ParseManualAsync(
        string documentId,
        string version,
        string filePath,
        List<string> notes,
        List<string> warnings,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken ct)
    {
        progress?.Report(new ImportProgressUpdate("文本抽取", 0, null, "正在解析文本层…"));
        var pages = await textExtractor.ExtractAsync(filePath, ct);
        progress?.Report(new ImportProgressUpdate("文本抽取", pages.Count, pages.Count, $"解析完成 {pages.Count} 页"));
        var totalChars = pages.Sum(p => p.Text.Length);
        var totalWords = pages.Sum(p => p.Words.Count);
        notes.Add($"文本链路：{pages.Count} 页，{totalChars} 字符，{totalWords} 个词项");

        if (totalChars == 0)
        {
            warnings.Add(
                "文本抽取结果为空：该 PDF 可能没有文字层（扫描件），或文字编码不受支持。"
                + "若确认是图片型文档，请把 documentType 改为宣传手册走视觉链路。");
        }

        return pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Text))
            .Select(page => new DocumentChunk
            {
                ChunkId = BuildChunkId(documentId, version, page.PageNo),
                DocumentId = documentId,
                Version = version,
                PageNo = page.PageNo,
                // 页级 bbox：整页范围，后续做段落级切片时会细化为词项并集
                Bbox = new BoundingBox(0, 0, page.Width, page.Height),
                Section = string.Empty,
                Text = page.Text,
            })
            .ToList();
    }

    /// <summary>宣传手册链路：逐页渲染 + 视觉识别，识别成功才产出一个内容块。</summary>
    private async Task<List<DocumentChunk>> ParseBrochureAsync(
        string documentId,
        string version,
        string filePath,
        List<string> notes,
        List<string> warnings,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken ct)
    {
        var rendered = 0;
        var recognized = 0;
        var errors = new List<string>();
        var chunks = new List<DocumentChunk>();

        await foreach (var image in pageRenderer.RenderAsync(filePath, ct))
        {
            rendered++;
            progress?.Report(new ImportProgressUpdate("视觉识别", rendered, null, $"正在识别第 {image.PageNo} 页…"));

            var result = await visionExtractor.RecognizeAsync(
                new VisionRequest(image.PageNo, image.PngBytes), ct);

            if (!result.Succeeded)
            {
                if (errors.Count < 5) errors.Add($"第 {image.PageNo} 页：{result.Error}");
                continue;
            }

            recognized++;
            chunks.Add(new DocumentChunk
            {
                ChunkId = BuildChunkId(documentId, version, image.PageNo),
                DocumentId = documentId,
                Version = version,
                PageNo = image.PageNo,
                Bbox = new BoundingBox(0, 0, image.PixelWidth, image.PixelHeight),
                Section = string.Empty,
                Text = result.Content,
            });
        }

        notes.Add($"视觉链路：渲染 {rendered} 页，识别成功 {recognized} 页");

        if (rendered == 0)
            warnings.Add("未能渲染出任何页面，请检查该 PDF 是否可正常打开。");
        else if (recognized == 0)
            warnings.Add("所有页面均未识别成功，请检查 Ai:Vision 配置与视觉模型服务是否可用。");

        if (errors.Count > 0)
            warnings.Add($"部分页面识别失败：{string.Join("；", errors)}");

        return chunks;
    }

    /// <summary>
    /// 批量向量化后写库。
    ///
    /// 向量化失败不中断导入：文本已经解析出来了，先落库保证内容不丢，
    /// 向量可以后续回填，但必须告警说明「本次检索走不到向量路」。
    /// </summary>
    private async Task<int> WriteChunksAsync(
        List<DocumentChunk> chunks,
        List<string> notes,
        List<string> warnings,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken ct)
    {
        if (chunks.Count == 0) return 0;

        float[][]? vectors = null;
        try
        {
            var embedded = await embeddingProvider.EmbedTextsAsync(chunks.Select(c => c.Text), ct);
            vectors = embedded.ToArray();
            notes.Add($"已向量化 {vectors.Length} 个块（{embeddingProvider.ModelName}，{embeddingProvider.Dimension} 维）");
        }
        catch (Exception ex)
        {
            warnings.Add($"向量化失败，本次仅写入文本（向量检索将检索不到这些块）：{ex.Message}");
        }

        var written = 0;
        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            var vector = vectors is not null && index < vectors.Length
                ? vectors[index]
                : Array.Empty<float>();

            await vectorStore.UpsertChunkAsync(chunk, vector, ct);
            written++;
            progress?.Report(new ImportProgressUpdate("向量化写入", written, chunks.Count, $"写入第 {written}/{chunks.Count} 个块"));
        }

        notes.Add($"已写入 {written} 个块");
        return written;
    }

    private static string BuildChunkId(string documentId, string version, int pageNo)
        => $"{documentId}:{version}:p{pageNo}";
}
