using Microsoft.Data.SqlClient;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>数据库初始器：按需建库、执行 schema.sql 建表，并校验向量列维度与配置一致。</summary>
public sealed class DatabaseInitializer(DbConfig db, AiOptions aiOptions)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(db.ConnectionString);

        // 建库（SQL Server 若无数据库则创建）
        var dbName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName)) dbName = "PdfRagQa";
        builder.InitialCatalog = "master";
        await using (var masterConn = new SqlConnection(builder.ConnectionString))
        {
            await masterConn.OpenAsync(ct);
            await using var createCmd = masterConn.CreateCommand();
            createCmd.CommandText = $"""
                IF DB_ID(N'{dbName}') IS NULL
                BEGIN
                    CREATE DATABASE [{dbName}];
                END
                """;
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        // 执行 schema.sql 建表（幂等：存在则跳过）
        var schema = await ReadSchemaSqlAsync(ct);
        await using var conn = db.CreateConnection();
        await conn.OpenAsync(ct);
        // schema.sql 含 GO 批处理分隔符，需按批执行
        foreach (var batch in SplitBatch(schema))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ValidateVectorDimensionAsync(conn, ct);
    }

    /// <summary>
    /// 校验 chunk.embedding 列的维度与配置一致。
    ///
    /// 维度不一致时向量检索会静默失效（距离算得出但结果无意义），且很难从症状反推原因，
    /// 因此在启动阶段直接报错——这也是需求文档 FR10「换 embedding 模型必须重建索引」的落地机制。
    /// </summary>
    private async Task ValidateVectorDimensionAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        // VECTOR(n) 的存储长度 = n × 4 字节 + 8 字节头，实测 VECTOR(3)=20 / VECTOR(256)=1032 / VECTOR(1536)=6152
        cmd.CommandText = """
            SELECT c.max_length
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'dbo.chunk') AND c.name = N'embedding';
            """;

        var raw = await cmd.ExecuteScalarAsync(ct);
        if (raw is null or DBNull) return;

        var maxLength = Convert.ToInt32(raw);
        if (maxLength < 8) return;

        var columnDimension = (maxLength - 8) / 4;
        if (columnDimension == aiOptions.EmbeddingDimensions) return;

        throw new InvalidOperationException(
            $"chunk.embedding 列维度为 {columnDimension}，但配置 Ai:EmbeddingDimensions = {aiOptions.EmbeddingDimensions}，两者必须一致。"
            + "维度不一致会让向量检索静默失效。请统一二者后重建全部向量："
            + "ALTER TABLE dbo.chunk ADD embedding VECTOR(<新维度>)（需先 DROP 旧列）→ 清空 embedding → 重新导入全部文档。");
    }

    private static async Task<string> ReadSchemaSqlAsync(CancellationToken ct)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "schema.sql");
        if (!File.Exists(path))
        {
            // 回退到源目录（开发环境）
            var root = Directory.GetCurrentDirectory();
            var alt = Path.Combine(root, "..", "..", "..", "..", "PdfRagQa.Infrastructure", "Data", "schema.sql");
            if (File.Exists(alt)) path = Path.GetFullPath(alt);
        }
        return await File.ReadAllTextAsync(path, ct);
    }

    private static IEnumerable<string> SplitBatch(string script)
    {
        // 按行切分，丢弃单独成行的批分隔符 GO（SQL Server 保留字，非可执行 SQL）
        var sb = new System.Text.StringBuilder();
        var lines = script.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (sb.Length > 0 && !string.IsNullOrWhiteSpace(sb.ToString()))
                    yield return sb.ToString();
                sb.Clear();
                continue;
            }
            sb.AppendLine(line);
        }
        if (sb.Length > 0 && !string.IsNullOrWhiteSpace(sb.ToString()))
            yield return sb.ToString();
    }
}