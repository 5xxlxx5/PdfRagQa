using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Services;
using PdfRagQa.Domain.Abstractions;
using PdfRagQa.Infrastructure.Data;
using PdfRagQa.Infrastructure.Embedding;
using PdfRagQa.Infrastructure.LLM;
using PdfRagQa.Infrastructure.Pdf;
using PdfRagQa.Infrastructure.Retrieval;
using PdfRagQa.Infrastructure.Storage;
using PdfRagQa.Infrastructure.Vision;

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

        // AI 服务配置：生成 / 视觉 / 向量化三项各自独立——
        // 实际部署中它们常来自不同厂商（例如生成用 DeepSeek、向量化用阿里百炼），地址与密钥都不相同
        var aiOptions = new AiOptions
        {
            Chat = ReadEndpoint(configuration, "Chat"),
            Vision = ReadEndpoint(configuration, "Vision"),
            Embedding = ReadEndpoint(configuration, "Embedding"),
            EmbeddingDimensions = ReadInt(configuration, "EmbeddingDimensions", 1024),
            EmbeddingBatchSize = ReadInt(configuration, "EmbeddingBatchSize", 10),
            RenderDpi = ReadInt(configuration, "RenderDpi", 120),
            VisionTimeoutSeconds = ReadInt(configuration, "VisionTimeoutSeconds", 180),
            EmbeddingTimeoutSeconds = ReadInt(configuration, "EmbeddingTimeoutSeconds", 120),
        };
        services.AddSingleton(aiOptions);

        // 数据库初始化依赖 AiOptions（启动时校验向量列维度与配置一致），故在此注册
        services.AddSingleton<DatabaseInitializer>();

        // 领域端口 -> SQL Server 实现（持久化）
        services.AddSingleton<IDocumentRepository, SqlServerDocumentRepository>();
        services.AddSingleton<IVectorStore, SqlServerChunkStore>();
        services.AddSingleton<IFeedbackRepository, SqlServerFeedbackRepository>();

        // 领域端口 -> 基础设施实现
        // 解析链路：手册走文本抽取，宣传册走「渲染 + 视觉识别」
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IPdfPageRenderer, PdfiumPageRenderer>();
        services.AddSingleton<IVisionExtractor, OpenAiCompatibleVisionExtractor>();
        services.AddSingleton<IEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>();

        services.AddSingleton<ILlmClient, OpenAiCompatibleChatClient>();
        services.AddSingleton<ICitationBuilder, CitationBuilder>();

        // 应用抽象 -> 基础设施实现
        services.AddScoped<IDocumentRouter, DefaultDocumentRouter>();
        services.AddScoped<IRagOrchestr, RagOrchestr>();
        // 混合检索：关键词 BM25 + 向量余弦双路，RRF 融合（需求文档 FR4）
        services.AddScoped<IHybridRetriever, RrfHybridRetriever>();
        services.AddScoped<IMultimodalRetriever, InMemoryMultimodalRetriever>();

        // 应用服务
        services.AddScoped<QuestionService>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<FeedbackService>();

        return services;
    }

    private static AiEndpointOptions ReadEndpoint(IConfiguration configuration, string name) => new()
    {
        BaseUrl = configuration[$"{AiOptions.SectionName}:{name}:BaseUrl"] ?? string.Empty,
        ApiKey = configuration[$"{AiOptions.SectionName}:{name}:ApiKey"] ?? string.Empty,
        Model = configuration[$"{AiOptions.SectionName}:{name}:Model"] ?? string.Empty,
    };

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[$"{AiOptions.SectionName}:{key}"], out var value) ? value : fallback;
}
