using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;

namespace PdfRagQa.Infrastructure;

/// <summary>
/// 路由实现：优先使用请求显式指定的文档类型，否则回退到 Manual（骨架）。
/// 后续应结合关键词/分类模型实现自动识别。
/// </summary>
public sealed class DefaultDocumentRouter : IDocumentRouter
{
    public Task<RouteDecision> RouteAsync(QuestionRequest request, CancellationToken ct = default)
    {
        var type = request.DocumentType ?? DocumentType.Manual;
        return Task.FromResult(new RouteDecision(type, JoinedConversationIds: []));
    }
}