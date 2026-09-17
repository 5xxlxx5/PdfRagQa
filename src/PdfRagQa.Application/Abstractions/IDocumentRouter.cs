using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;

namespace PdfRagQa.Application.Abstractions;

/// <summary>一条问答的路由决议：进入哪条 RAG 链路。</summary>
public sealed record RouteDecision(DocumentType Type, IReadOnlyList<string> JoinedConversationIds);

/// <summary>文档路由端口：把查询路由到最适配的检索链路。</summary>
public interface IDocumentRouter
{
    Task<RouteDecision> RouteAsync(QuestionRequest request, CancellationToken ct = default);
}