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

/// <summary>大模型生成结果（含算法返回的对应用原始引用索引，用于溯源）。</summary>
public sealed class LlmResponse
{
    public string Answer { get; init; } = string.Empty;
    public IReadOnlyList<int> CachedChunkIndexes { get; init; } = [];
}

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}