using System.Data.Common;
using Dapper;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>SQL Server 文档元数据仓库（Dapper）。</summary>
public sealed class SqlServerDocumentRepository(DbConfig db) : IDocumentRepository
{
    public async Task UpsertAsync(Document document, CancellationToken ct = default)
    {
        const string sql = """
            MERGE dbo.document WITH (HOLDLOCK) AS t
            USING (SELECT @DocumentId AS document_id, @Version AS version) AS s
              ON t.document_id = s.document_id AND t.version = s.version
            WHEN MATCHED THEN
                UPDATE SET title=@Title, doc_type=@Type, [language]=@Lang,
                           source_file=@SourceFile, imported_at=@ImportedAt, content_hash=@ContentHash
            WHEN NOT MATCHED THEN
                INSERT (document_id, version, title, doc_type, [language], source_file, is_latest, imported_at, content_hash)
                VALUES (@DocumentId, @Version, @Title, @Type, @Lang, @SourceFile, @IsLatest, @ImportedAt, @ContentHash);
            """;

        await using var conn = db.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            document.DocumentId,
            document.Version,
            document.Title,
            Type = (int)document.Type,
            Lang = (int)document.Language,
            document.SourceFile,
            document.IsLatest,
            document.ImportedAt,
            ContentHash = (string?)document.SourceFile is null ? null : SimpleHash(document.SourceFile),
        }, transaction: null, commandTimeout: 30)
            .ConfigureAwait(false);
    }

    public async Task<Document?> GetAsync(string documentId, string? version = null, CancellationToken ct = default)
    {
        var sql = version is null
            ? "SELECT TOP (1) * FROM dbo.document WHERE document_id=@DocumentId ORDER BY is_latest DESC, imported_at DESC;"
            : "SELECT * FROM dbo.document WHERE document_id=@DocumentId AND version=@Version;";
        await using var conn = db.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<DocumentRow>(sql, new { DocumentId = documentId, Version = version })
            .ConfigureAwait(false);
        return row is null ? null : row.ToDomain();
    }

    public async Task<Document?> GetLatestAsync(string documentId, CancellationToken ct = default)
        => await GetAsync(documentId, version: null, ct);

    public async Task<IReadOnlyList<Document>> QueryAsync(
        string? documentId = null, string? version = null, DocumentType? type = null, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM dbo.document WHERE 1=1";
        if (documentId is not null) sql += " AND document_id=@DocumentId";
        if (version is not null) sql += " AND version=@Version";
        if (type is not null) sql += " AND doc_type=@Type";
        sql += " ORDER BY document_id, imported_at DESC;";

        await using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<DocumentRow>(sql, new { DocumentId = documentId, Version = version, Type = type.HasValue ? (int)type.Value : (int?)null })
            .ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private static string SimpleHash(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // SQL 行映射记录（避免 Dapper 与领域实体耦合标签）
    private sealed record DocumentRow(
        string Document_id, string Version, string Title, int Doc_type, int Language,
        string? Source_file, bool Is_latest, DateTimeOffset Imported_at, string? Content_hash)
    {
        public Document ToDomain() => new()
        {
            DocumentId = Document_id,
            Version = Version,
            Title = Title,
            Type = (DocumentType)Doc_type,
            Language = (LanguageCode)Language,
            SourceFile = Source_file ?? string.Empty,
            IsLatest = Is_latest,
            ImportedAt = Imported_at,
        };
    }
}