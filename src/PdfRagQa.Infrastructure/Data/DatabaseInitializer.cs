using Microsoft.Data.SqlClient;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>数据库初始器：按需建库并执行 schema.sql 创建表。</summary>
public sealed class DatabaseInitializer(DbConfig db)
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