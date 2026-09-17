using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Application.Services;

/// <summary>
/// 统一问答入口（FR2 路由 + FR3 版本/语言过滤 + FR8 多轮）。
/// 流程：路由 -> 按文档类型检索 -> 编排生成。
/// </summary>
public sealed class QuestionService(
    IDocumentRouter router,
    IHybridRetriever hybridRetriever,
    IMultimodalRetriever multimodalRetriever,
    IRagOrchestr orchestrator)
{
    public async Task<QuestionResponse> AskAsync(QuestionRequest request, CancellationToken ct = default)
    {
        // 1. 路由：决定进入哪条 RAG 链路
        var decision = await router.RouteAsync(request, ct);

        // 2. 按文档类型检索
        IReadOnlyList<DocumentChunk> context = decision.Type == DocumentType.Manual
            ? await hybridRetriever.RetrieveAsync(new HybridQuery
            {
                Query = request.Question,
                DocumentId = request.DocumentId,
                Version = request.Version,
                Language = request.Language,
            }, ct)
            : await multimodalRetriever.RetrieveAsync(new MultimodalQuery
            {
                Query = request.Question,
                DocumentId = request.DocumentId,
                Version = request.Version,
                Language = request.Language,
            }, ct);

        // 3. 生成（多轮历史由上层注入；此处骨架先传 null）
        return await orchestrator.AnswerAsync(request, context, history: null, ct);
    }
}