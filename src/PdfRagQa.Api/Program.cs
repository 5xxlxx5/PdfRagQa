using PdfRagQa.Infrastructure;
using PdfRagQa.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 注册基础设施层（含 SQL Server 持久化实现）
builder.Services.AddRagInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // 仅在非开发环境强制 HTTPS 跳转。
    //
    // 开发用的 http 配置档没有 HTTPS 端点，中间件无法确定跳转端口，
    // 启动后首次请求会打印一条「Failed to determine the https port for redirect」告警，
    // 且实际不做任何跳转——纯噪音，掩盖真正需要注意的日志。
    //
    // 部署注意：若 TLS 终止在反向代理（Nginx 等），还需配置 ForwardedHeaders，
    // 否则应用看到的是 http，会形成重定向死循环。ForwardedHeaders 必须限定可信代理
    // （KnownProxies / KnownNetworks），不能无条件信任请求头，否则可被伪造。
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();

// 启动时初始化数据库（建库建表，幂等）
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();