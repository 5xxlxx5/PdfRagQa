using System.Data.Common;
using Dapper;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>
/// SQL Server 文本/向量存储（Dapper）。
/// 标量元数据与关键词检索落库；向量检索暂用关键词近似（vector_json 占位，
/// 后续接入 Milvus/Qdrant 等独立向量库时替换本实现）。
/// </summary>
public sealed class SqlServerChunkStore(DbConfig db) : IVectorStore
{
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
        // 简单全文近似：分词后按出现次数计分
        var words = query.Split(new[] { ' ', ',', '，', '。', '；' }, StringSplitOptions.RemoveEmptyEntries);
        var sql = new System.Text.StringBuilder(
            "WITH scored AS (SELECT chunk_id FROM dbo.chunk WHERE 1=1 ");
        if (documentId is not null) sql.Append("AND document_id=@DocumentId ");
        if (version is not null) sql.Append("AND version=@Version ");
        if (language is not null && language != LanguageCode.ZhEn) sql.Append("AND [language]=@Lang ");
        sql.AppendLine(") SELECT TOP (@TopK) c.* FROM dbo.chunk c JOIN scored ON c.chunk_id=scored.chunk_id ");
        // 计分仅用于演示：此处按词出现次数
        sql.Append("ORDER BY (SELECT COUNT(*) FROM STRING_SPLIT(c.[text], ' ') WHERE value IN @Words) DESC;");

        await using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<ChunkRow>(sql.ToString(), new
        {
            TopK = topK,
            DocumentId = documentId,
            Version = version,
            Lang = language.HasValue ? (int)language.Value : (int?)null,
            Words = words,
        }).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<DocumentChunk>> VectorSearchAsync(
        float[] queryVector, int topK, string? documentId, string? version, LanguageCode? language, CancellationToken ct = default)
        => await KeywordSearchAsync("", topK, documentId, version, language, ct); // 占位：向量库接入后实现

    public async Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken ct = default)
    {
        await using var conn = db.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ChunkRow>(
            "SELECT * FROM dbo.chunk WHERE chunk_id=@ChunkId;", new { ChunkId = chunkId }).ConfigureAwait(false);
        return row?.ToDomain();
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