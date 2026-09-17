using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>数据库配置：封装连接字符串与连接工厂（Dapper 使用 SqlConnection）。</summary>
public sealed class DbConfig(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public DbConnection CreateConnection()
        => new SqlConnection(ConnectionString);
}