using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure;

/// <summary>
/// RAG 编排器实现：组装上下文 -> 调用 LLM -> 用 algorithm 返回的索引构造 Citations。
/// 负责生成 QuestionResponse（含 answer / citations / imageRefs / confidence）。
/// </summary>
public sealed class RagOrchestr(
    ILlmClient llm,
    ICitationBuilder citationBuilder) : IRagOrchestr
{
    public async Task<QuestionResponse> AnswerAsync(
        QuestionRequest request,
        IReadOnlyList<DocumentChunk> context,
        IReadOnlyList<ChatTurn>? history,
        CancellationToken ct = default)
    {
        var llmResponse = await llm.CompleteAsync(new LlmRequest
        {
            UserPrompt = request.Question,
            Context = context,
            History = history,
        }, ct);

        var citations = citationBuilder.BuildCitations(context, llmResponse.CachedChunkIndexes);

        return new QuestionResponse
        {
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString("N"),
            Answer = llmResponse.Answer,
            Citations = citations.Select(ToDto).ToList(),
            ImageRefs = context.Where(c => c.ImageRef is not null).Select(c => c.ImageRef!).ToList(),
            Confidence = context.Count == 0 ? 0f : citations.Count / (float)Math.Max(context.Count, 1),
            DocumentType = request.DocumentType?.ToString() ?? DocumentType.Manual.ToString(),
            Version = request.Version ?? "",
        };
    }

    private static CitationDto ToDto(Citation c) => new()
    {
        DocumentId = c.DocumentId,
        DocumentTitle = c.DocumentTitle,
        Version = c.Version,
        ChunkId = c.ChunkId,
        PageNo = c.PageNo,
        Section = c.Section,
        Bbox = c.Bbox is null ? null : new BoundingBoxDto(c.Bbox.Value.X, c.Bbox.Value.Y, c.Bbox.Value.Width, c.Bbox.Value.Height),
    };
}