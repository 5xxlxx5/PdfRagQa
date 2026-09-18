using System.Data.Common;
using System.Globalization;
using System.Text;
using Dapper;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;
using PdfRagQa.Infrastructure.Retrieval;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>
/// SQL Server 文本/向量存储（Dapper）。
///
/// 关键词检索：中文 bigram 分词 → 语料统计 → LIKE 粗筛候选 → 内存 BM25 精排。
/// 向量检索：SQL Server 2025 原生 VECTOR 类型 + VECTOR_DISTANCE 余弦距离，
///          因此不需要引入 Milvus / Qdrant 等独立向量库。
/// </summary>
public sealed class SqlServerChunkStore(DbConfig db, AiOptions aiOptions) : IVectorStore
{
    /// <summary>粗筛候选上限：兜住「检索词极常见」时结果集过大的情况。</summary>
    private const int CandidateLimit = 2000;

    private readonly int _dimensions = aiOptions.EmbeddingDimensions;

    /// <summary>生成向量所用的模型名。与向量同源，故由本类维护，不依赖调用方传入。</summary>
    private readonly string? _embeddingModel =
        string.IsNullOrWhiteSpace(aiOptions.Embedding.Model) ? null : aiOptions.Embedding.Model;

    public async Task UpsertChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct = default)
    {
        var hasVector = vector is { Length: > 0 };
        if (hasVector && vector.Length != _dimensions)
        {
            throw new InvalidOperationException(
                $"向量维度 {vector.Length} 与配置 Ai:EmbeddingDimensions = {_dimensions} 不一致，拒绝写入。");
        }

        // 维度必须写成字面量，VECTOR(@p) 不被支持；_dimensions 来自配置的 int，无注入风险
        var embeddingExpr = hasVector ? $"CAST(@EmbeddingText AS VECTOR({_dimensions}))" : "NULL";

        var sql = $"""
            MERGE dbo.chunk WITH (HOLDLOCK) AS t
            USING (SELECT @ChunkId AS chunk_id) AS s ON t.chunk_id = s.chunk_id
            WHEN MATCHED THEN
                UPDATE SET document_id=@DocumentId, version=@Version, [language]=@Lang, page_no=@PageNo,
                           bbox_x=@BboxX, bbox_y=@BboxY, bbox_w=@BboxW, bbox_h=@BboxH, section=@Section,
                           [text]=@Text, table_markdown=@TableMarkdown, image_ref=@ImageRef,
                           image_description=@ImageDescription, embedding_model=@EmbeddingModel,
                           dimension=@Dimension, vector_json=NULL, embedding={embeddingExpr}
            WHEN NOT MATCHED THEN
                INSERT (chunk_id, document_id, version, [language], page_no, bbox_x, bbox_y, bbox_w, bbox_h,
                        section, [text], table_markdown, image_ref, image_description, embedding_model, dimension,
                        vector_json, embedding)
                VALUES (@ChunkId, @DocumentId, @Version, @Lang, @PageNo, @BboxX, @BboxY, @BboxW, @BboxH,
                        @Section, @Text, @TableMarkdown, @ImageRef, @ImageDescription, @EmbeddingModel, @Dimension,
                        NULL, {embeddingExpr});
            """;

        await using var conn = db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            chunk.ChunkId,
            chunk.DocumentId,
            chunk.Version,
            // 注意：此处仍硬编码 Zh —— DocumentChunk 尚无语言字段，属已知遗留
            Lang = (int)LanguageCode.Zh,
            chunk.PageNo,
            BboxX = chunk.Bbox?.X,
            BboxY = chunk.Bbox?.Y,
            BboxW = chunk.Bbox?.Width,
            BboxH = chunk.Bbox?.Height,
            chunk.Section,
            Text = chunk.Text,
            TableMarkdown = chunk.TableMarkdown,
            chunk.ImageRef,
            chunk.ImageDescription,
            // embedding_model 与 dimension 跟随向量一起写：没有向量时留 NULL，
            // 避免出现「记录了模型版本却没有向量」这种自相矛盾的状态（需求文档 FR10）
            EmbeddingModel = hasVector ? _embeddingModel : null,
            Dimension = hasVector ? _dimensions : (int?)null,
            EmbeddingText = hasVector ? ToVectorLiteral(vector) : null,
        }, commandTimeout: 30, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentChunk>> KeywordSearchAsync(
        string query, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
    {
        if (topK <= 0) return Array.Empty<DocumentChunk>();

        var terms = QueryTokenizer.Tokenize(query);
        if (terms.Count == 0) return Array.Empty<DocumentChunk>();

        var filter = BuildFilter(documentId, version, language);

        await using var conn = db.CreateConnection();

        // 1) 语料统计：一次查询同时拿到 chunk 总数、平均长度与每个检索词的文档频率
        var stats = await LoadCorpusStatsAsync(conn, filter, terms, documentId, version, language, ct)
            .ConfigureAwait(false);
        if (stats is null || stats.DocumentCount == 0) return Array.Empty<DocumentChunk>();

        // 2) 粗筛：只取至少命中一个检索词的行，避免把整表拉进内存
        var candidates = await LoadCandidatesAsync(conn, filter, terms, documentId, version, language, ct)
            .ConfigureAwait(false);

        // 3) 精排：BM25 计分放在内存里做，便于调参与单独测试
        return candidates
            .Select(chunk => (Chunk: chunk, Score: Bm25Scorer.Score(chunk.Text, terms, stats)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();
    }

    /// <summary>
    /// 向量检索：SQL Server 2025 原生 VECTOR_DISTANCE 余弦距离。
    /// 未建向量索引时是暴力扫描 + 排序，对万级 chunk 足够；数据量上去后再评估向量索引。
    /// </summary>
    public async Task<IReadOnlyList<DocumentChunk>> VectorSearchAsync(
        float[] queryVector, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
    {
        if (topK <= 0 || queryVector.Length == 0) return Array.Empty<DocumentChunk>();
        if (queryVector.Length != _dimensions)
        {
            throw new InvalidOperationException(
                $"查询向量维度 {queryVector.Length} 与配置 Ai:EmbeddingDimensions = {_dimensions} 不一致。");
        }

        var filter = BuildFilter(documentId, version, language);
        var sql = $"SELECT TOP (@TopK) {Columns()} {filter} AND c.embedding IS NOT NULL "
                  + $"ORDER BY VECTOR_DISTANCE('cosine', c.embedding, CAST(@QueryVector AS VECTOR({_dimensions})));";

        var args = BuildFilterArgs(documentId, version, language);
        args.Add("TopK", topK);
        args.Add("QueryVector", ToVectorLiteral(queryVector));

        await using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<ChunkRow>(
            new CommandDefinition(sql, args, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken ct = default)
    {
        await using var conn = db.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ChunkRow>(
            new CommandDefinition(
                $"SELECT {Columns()} {FromClause} WHERE c.chunk_id = @ChunkId;",
                new { ChunkId = chunkId },
                cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToDomain();
    }

    /// <summary>
    /// 检索结果需要的列，显式列出而不 SELECT *：
    /// VECTOR 列映射到实体没有意义，也可能在读取时触发类型转换异常。
    /// 同时关联出文档标题——引用展示需要它（需求文档 FR6），反规范化避免逐条回查。
    /// </summary>
    private static string Columns() =>
        string.Join(", ", new[]
        {
            "chunk_id", "document_id", "version", "[language]", "page_no",
            "bbox_x", "bbox_y", "bbox_w", "bbox_h", "section", "[text]",
            "table_markdown", "image_ref", "image_description", "embedding_model", "dimension",
        }.Select(c => $"c.{c}")) + ", d.title AS document_title";

    /// <summary>统一的 FROM 子句。用 LEFT JOIN 取文档标题，同时替代原先的 EXISTS 最新版判断。</summary>
    private const string FromClause =
        "FROM dbo.chunk c LEFT JOIN dbo.document d"
        + " ON d.document_id = c.document_id AND d.version = c.version";

    /// <summary>把 float[] 转成 SQL Server VECTOR 接受的字面量，如 [0.1,0.2,0.3]。</summary>
    private static string ToVectorLiteral(float[] vector)
    {
        var sb = new StringBuilder(vector.Length * 12).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }

    /// <summary>
    /// 构造过滤条件（以 FROM 子句开头）。
    /// 未显式指定版本时默认限定到最新版（需求文档 FR3）——用关联出的 document.is_latest 判断，
    /// 原先用 EXISTS 子查询是因为没有关联 document 表，现在为了取标题已经 JOIN，不必再套一层。
    /// </summary>
    private static string BuildFilter(string? documentId, string? version, LanguageCode? language)
    {
        var sql = new StringBuilder(FromClause).Append(" WHERE 1=1");

        if (documentId is not null)
            sql.Append(" AND c.document_id = @DocumentId");

        if (version is not null)
            sql.Append(" AND c.version = @Version");
        else
            sql.Append(" AND d.is_latest = 1");

        if (language is not null && language != LanguageCode.ZhEn)
            sql.Append(" AND c.[language] = @Lang");

        return sql.ToString();
    }

    /// <summary>过滤参数（每个查询各建一份，避免参数串用）。</summary>
    private static DynamicParameters BuildFilterArgs(
        string? documentId, string? version, LanguageCode? language)
    {
        var args = new DynamicParameters();
        if (documentId is not null) args.Add("DocumentId", documentId);
        if (version is not null) args.Add("Version", version);
        if (language is not null && language != LanguageCode.ZhEn) args.Add("Lang", (int)language.Value);
        return args;
    }

    private static async Task<CorpusStats?> LoadCorpusStatsAsync(
        DbConnection conn,
        string filter,
        IReadOnlyList<string> terms,
        string? documentId,
        string? version,
        LanguageCode? language,
        CancellationToken ct)
    {
        // 每个词的文档频率用条件聚合一次算出，再用 CONCAT 拼成一列返回。
        // 不用动态行（DapperRow）：非泛型重载返回 object，运行时索引器会抛 RuntimeBinderException。
        var dfParts = new List<string>(terms.Count);
        for (var i = 0; i < terms.Count; i++)
            dfParts.Add($"SUM(CASE WHEN c.[text] LIKE @Like{i} THEN 1 ELSE 0 END)");

        var sql = new StringBuilder("SELECT COUNT(*) AS DocCount, ")
            .Append("ISNULL(AVG(CAST(LEN(c.[text]) AS FLOAT)), 0) AS AvgLen, ")
            // CONCAT 至少需要 2 个参数，单检索词时补一个空串占位
            .Append("CONCAT('', ").Append(string.Join(", ',', ", dfParts)).Append(") AS DfList ")
            .Append(filter);

        var args = BuildFilterArgs(documentId, version, language);
        for (var i = 0; i < terms.Count; i++)
            args.Add($"Like{i}", $"%{EscapeLike(terms[i])}%");

        var row = await conn.QuerySingleOrDefaultAsync<CorpusStatsRow>(
            new CommandDefinition(sql.ToString(), args, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return null;

        var dfValues = (row.DfList ?? string.Empty).Split(',');
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < terms.Count; i++)
        {
            df[terms[i]] = i < dfValues.Length && int.TryParse(dfValues[i], out var value) ? value : 0;
        }

        return new CorpusStats(row.DocCount, row.AvgLen, df);
    }

    private static async Task<IReadOnlyList<DocumentChunk>> LoadCandidatesAsync(
        DbConnection conn,
        string filter,
        IReadOnlyList<string> terms,
        string? documentId,
        string? version,
        LanguageCode? language,
        CancellationToken ct)
    {
        var likes = new List<string>(terms.Count);
        for (var i = 0; i < terms.Count; i++)
            likes.Add($"c.[text] LIKE @Like{i}");

        var sql = $"SELECT TOP (@CandidateLimit) {Columns()} {filter} AND ({string.Join(" OR ", likes)})";

        var args = BuildFilterArgs(documentId, version, language);
        for (var i = 0; i < terms.Count; i++)
            args.Add($"Like{i}", $"%{EscapeLike(terms[i])}%");
        args.Add("CandidateLimit", CandidateLimit);

        var rows = await conn.QueryAsync<ChunkRow>(
            new CommandDefinition(sql, args, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToDomain()).ToList();
    }

    /// <summary>转义 LIKE 通配符，避免用户输入的 % 或 _ 被当成模式（SQL Server 用方括号转义）。</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    /// <summary>语料统计的查询结果（df 列表以逗号拼接，避免动态行）。</summary>
    private sealed class CorpusStatsRow
    {
        public int DocCount { get; set; }
        public double AvgLen { get; set; }
        public string? DfList { get; set; }
    }

    // SQL 行映射（用属性映射而非位置记录：可空列 + 额外列会让位置记录的严格构造签名匹配失败）
    private sealed class ChunkRow
    {
        public string Chunk_id { get; set; } = string.Empty;
        public string Document_id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Document_title { get; set; }
        public int Language { get; set; }
        public int Page_no { get; set; }
        public double? Bbox_x { get; set; }
        public double? Bbox_y { get; set; }
        public double? Bbox_w { get; set; }
        public double? Bbox_h { get; set; }
        public string? Section { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Table_markdown { get; set; }
        public string? Image_ref { get; set; }
        public string? Image_description { get; set; }
        public string? Embedding_model { get; set; }
        public int? Dimension { get; set; }

        public DocumentChunk ToDomain() => new()
        {
            ChunkId = Chunk_id,
            DocumentId = Document_id,
            Version = Version,
            DocumentTitle = Document_title ?? string.Empty,
            PageNo = Page_no,
            Bbox = Bbox_x is null ? null : new BoundingBox(Bbox_x.Value, Bbox_y ?? 0, Bbox_w ?? 0, Bbox_h ?? 0),
            Section = Section ?? string.Empty,
            Text = Text,
            TableMarkdown = Table_markdown,
            ImageRef = Image_ref,
            ImageDescription = Image_description,
            EmbeddingModel = Embedding_model ?? string.Empty,
            Dimension = Dimension ?? 0,
        };
    }
}
