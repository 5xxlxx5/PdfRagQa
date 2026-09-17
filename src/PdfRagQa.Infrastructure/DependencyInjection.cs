using Microsoft.Extensions.DependencyInjection;
using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Services;
using PdfRagQa.Domain.Abstractions;
using PdfRagQa.Infrastructure.LLM;
using PdfRagQa.Infrastructure.Pdf;
using PdfRagQa.Infrastructure.Retrieval;
using PdfRagQa.Infrastructure.Storage;

namespace PdfRagQa.Infrastructure;

/// <summary>基础设施层依赖注入注册。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddRagInfrastructure(this IServiceCollection services)
    {
        // 领域端口 -> 基础设施实现（占位）
        services.AddSingleton<IPdfParser, PlaceholderPdfParser>();
        services.AddSingleton<IDocumentClassifier, HeuristicDocumentClassifier>();
        services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        services.AddSingleton<IFeedbackRepository, InMemoryFeedbackRepository>();
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