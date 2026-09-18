namespace PdfRagQa.Domain.Abstractions;

/// <summary>大模型生成端口。</summary>
public sealed class LlmRequest
{
    public string UserPrompt { get; init; } = string.Empty;
    public IReadOnlyList<DocumentChunk> Context { get; init; } = [];
    public IReadOnlyList<ChatTurn>? History { get; init; }
    public string? SystemPrompt { get; init; }
}

/// <summary>多轮对话中的历史消息。</summary>
public sealed record ChatTurn(string Role, string Content);

/// <summary>
/// 大模型生成结果。
/// <see cref="UsedContextIndexes"/> 是模型在答案中标注引用的上下文片段序号（对应 LlmRequest.Context 的下标），
/// 用于构造结构化引用；模型没有标注任何引用时为空集，此时不应凭空生成引用。
/// </summary>
public sealed class LlmResponse
{
    public string Answer { get; init; } = string.Empty;
    public IReadOnlyList<int> UsedContextIndexes { get; init; } = [];
}

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
