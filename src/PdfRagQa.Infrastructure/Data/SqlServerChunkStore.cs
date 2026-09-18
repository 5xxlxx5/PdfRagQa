using System.Data.Common;
using System.Text;
using Dapper;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;
using PdfRagQa.Infrastructure.Retrieval;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>
/// SQL Server 文本/向量存储（Dapper）。
///
/// 关键词检索为真实实现：中文 bigram 分词 → 语料统计 → LIKE 粗筛候选 → 内存 BM25 精排。
/// 向量检索尚未实现（接入 IEmbeddingProvider 后替换），当前返回空集而不是拿关键词结果冒充。
/// </summary>
public sealed class SqlServerChunkStore(DbConfig db) : IVectorStore
{
    /// <summary>粗筛候选上限：兜住「检索词极常见」时结果集过大的情况。</summary>
    private const int CandidateLimit = 2000;

    public async Task UpsertChunkAsync(DocumentChunk chunk, float[] vector, CancellationToken ct = default)
    {
        const string sql = """
            MERGE dbo.chunk WITH (HOLDLOCK) AS t
            USING (SELECT @ChunkId AS chunk_id) AS s ON t.chunk_id = s.chunk_id
            WHEN MATCHED THEN
                UPDATE SET document_id=@DocumentId, version=@Version, [language]=@Lang, page_no=@PageNo,
                           bbox_x=@BboxX, bbox_y=@BboxY, bbox_w=@BboxW, bbox_h=@BboxH, section=@Section,
                           [text]=@Text, table_markdown=@TableMarkdown, image_ref=@ImageRef,
                           image_description=@ImageDescription, embedding_model=@EmbeddingModel,
                           dimension=@Dimension, vector_json=@VectorJson
            WHEN NOT MATCHED THEN
                INSERT (chunk_id, document_id, version, [language], page_no, bbox_x, bbox_y, bbox_w, bbox_h,
                        section, [text], table_markdown, image_ref, image_description, embedding_model, dimension, vector_json)
                VALUES (@ChunkId, @DocumentId, @Version, @Lang, @PageNo, @BboxX, @BboxY, @BboxW, @BboxH,
                        @Section, @Text, @TableMarkdown, @ImageRef, @ImageDescription, @EmbeddingModel, @Dimension, @VectorJson);
            """;

        await using var conn = db.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            chunk.ChunkId,
            chunk.DocumentId,
            chunk.Version,
            Lang = (int)LanguageCode.Zh,
            chunk.PageNo,
            BboxX = chunk.Bbox?.X, BboxY = chunk.Bbox?.Y, BboxW = chunk.Bbox?.Width, BboxH = chunk.Bbox?.Height,
            chunk.Section,
            Text = chunk.Text,
            TableMarkdown = chunk.TableMarkdown,
            chunk.ImageRef,
            chunk.ImageDescription,
            chunk.EmbeddingModel,
            chunk.Dimension,
            // 向量为空时写 NULL 而不是空字符串，便于区分「未生成向量」与「向量异常」
            VectorJson = vector is { Length: > 0 } ? string.Join(',', vector) : null,
        }, transaction: null, commandTimeout: 30).ConfigureAwait(false);
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
    /// 构造过滤条件（以 "FROM dbo.chunk c WHERE 1=1" 开头）。
    /// 未显式指定版本时默认限定到最新版（需求文档 FR3）——通过 document 表的 is_latest 标记。
    /// </summary>
    private static string BuildFilter(string? documentId, string? version, LanguageCode? language)
    {
        var sql = new StringBuilder("FROM dbo.chunk c WHERE 1=1");

        if (documentId is not null)
            sql.Append(" AND c.document_id = @DocumentId");

        if (version is not null)
            sql.Append(" AND c.version = @Version");
        else
            sql.Append(" AND EXISTS (SELECT 1 FROM dbo.document d"
                       + " WHERE d.document_id = c.document_id AND d.version = c.version AND d.is_latest = 1)");

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

        var sql = $"SELECT TOP (@CandidateLimit) c.* {filter} AND ({string.Join(" OR ", likes)})";

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

    /// <summary>
    /// 向量检索尚未实现：接入 IEmbeddingProvider 后按余弦相似度实现。
    /// 此处返回空集，而不是拿关键词结果冒充向量结果——避免上层误判「向量路已经可用」。
    /// </summary>
    public Task<IReadOnlyList<DocumentChunk>> VectorSearchAsync(
        float[] queryVector, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());

    public async Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken ct = default)
    {
        await using var conn = db.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ChunkRow>(
            "SELECT * FROM dbo.chunk WHERE chunk_id=@ChunkId;", new { ChunkId = chunkId }).ConfigureAwait(false);
        return row?.ToDomain();
    }

    /// <summary>语料统计的查询结果（df 列表以逗号拼接，避免动态行）。</summary>
    private sealed class CorpusStatsRow
    {
        public int DocCount { get; set; }
        public double AvgLen { get; set; }
        public string? DfList { get; set; }
    }

    // SQL 行映射（用属性映射而非位置记录：可空列 + SELECT * 的额外列
    // 会让位置记录的严格构造签名匹配失败，Dapper 直接抛异常）
    private sealed class ChunkRow
    {
        public string Chunk_id { get; set; } = string.Empty;
        public string Document_id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
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