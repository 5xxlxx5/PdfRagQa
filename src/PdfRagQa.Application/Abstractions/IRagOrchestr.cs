using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Application.Abstractions;

/// <summary>
/// RAG 编排器：负责组装上下文、调用 LLM、构造引用，按文档类型选择检索链路。
/// QuestionService -> RagOrchestr -> Retriever(接口注入)。
/// </summary>
public interface IRagOrchestr
{
    Task<QuestionResponse> AnswerAsync(
        QuestionRequest request,
        IReadOnlyList<DocumentChunk> context,
        IReadOnlyList<ChatTurn>? history,
        CancellationToken ct = default);
}