using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Services;
using PdfRagQa.Domain.Abstractions;
using PdfRagQa.Infrastructure.Data;
using PdfRagQa.Infrastructure.LLM;
using PdfRagQa.Infrastructure.Pdf;
using PdfRagQa.Infrastructure.Retrieval;
using PdfRagQa.Infrastructure.Storage;

namespace PdfRagQa.Infrastructure;

/// <summary>基础设施层依赖注入注册。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddRagInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 数据库配置（连接串取自 appsettings，缺省为 LocalDB 开发连接串）
        var connectionString = configuration.GetConnectionString("PdfRagQa")
                               ?? "Server=(localdb)\\MSSQLLocalDB;Database=PdfRagQa;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        services.AddSingleton(new DbConfig(connectionString));
        services.AddSingleton<DatabaseInitializer>();

        // 领域端口 -> SQL Server 实现（持久化）
        services.AddSingleton<IDocumentRepository, SqlServerDocumentRepository>();
        services.AddSingleton<IVectorStore, SqlServerChunkStore>();
        services.AddSingleton<IFeedbackRepository, SqlServerFeedbackRepository>();

        // 领域端口 -> 基础设施实现（占位）
        services.AddSingleton<IPdfParser, PlaceholderPdfParser>();
        services.AddSingleton<IDocumentClassifier, HeuristicDocumentClassifier>();
        services.AddSingleton<ILlmClient, StubLlmClient>();
        services.AddSingleton<ICitationBuilder, CitationBuilder>();

        // 应用抽象 -> 基础设施实现
        services.AddScoped<IDocumentRouter, DefaultDocumentRouter>();
        services.AddScoped<IRagOrchestr, RagOrchestr>();
        services.AddScoped<IHybridRetriever, InMemoryHybridRetriever>();
        services.AddScoped<IMultimodalRetriever, InMemoryMultimodalRetriever>();

        // 应用服务
        services.AddScoped<QuestionService>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<FeedbackService>();

        return services;
    }
}