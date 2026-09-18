using PdfRagQa.Application.Abstractions;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure;

/// <summary>
/// RAG 编排器实现：组装上下文 → 调用生成模型 → 按模型标注的序号构造 Citations。
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

        var citations = citationBuilder.BuildCitations(context, llmResponse.UsedContextIndexes);

        return new QuestionResponse
        {
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString("N"),
            Answer = llmResponse.Answer,
            Citations = citations.Select(ToDto).ToList(),
            ImageRefs = context.Where(c => c.ImageRef is not null).Select(c => c.ImageRef!).ToList(),
            Confidence = ComputeConfidence(citations.Count, context.Count),
            DocumentType = context.Count > 0 ? context[0].DocumentId : (request.DocumentType?.ToString() ?? DocumentType.Manual.ToString()),
            Version = request.Version ?? (context.Count > 0 ? context[0].Version : string.Empty),
        };
    }

    /// <summary>
    /// 置信度 = 模型标注引用的片段数 / 提供给模型的上下文片段数，即「上下文被用上了多少」。
    ///
    /// 这不是校准过的概率，只是覆盖率——但它是可解释的：检索召回 20 条而答案只依据 2 条，
    /// 说明要么召回噪声大，要么问题只命中少数内容，两种情况下用户都该更谨慎。
    /// 注意必须在 CitationBuilder 只返回「模型真正引用」的片段之后才有意义；
    /// 此前它在模型未标注引用时退回「全部上下文」，导致该值恒为 1，完全失去指示作用。
    ///
    /// 待评估集建立后（需求文档 §9.2 RAGAS），应替换为基于 faithfulness / relevancy 的校准值。
    /// </summary>
    private static float ComputeConfidence(int citedCount, int contextCount)
        => contextCount <= 0 ? 0f : citedCount / (float)contextCount;

    private static CitationDto ToDto(Citation c) => new()
    {
        DocumentId = c.DocumentId,
        DocumentTitle = c.DocumentTitle,
        Version = c.Version,
        ChunkId = c.ChunkId,
        PageNo = c.PageNo,
        Section = c.Section,
        Bbox = c.Bbox is null ? null : new BoundingBoxDto(c.Bbox.Value.X, c.Bbox.Value.Y, c.Bbox.Value.Width, c.Bbox.Value.Height),
        TextPreview = c.TextPreview,
    };
}
