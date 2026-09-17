using System.Text;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.LLM;

/// <summary>
/// 占位 LLM 客户端：返回检索上下文中与关键词匹配的片段，便于本地跑通链路。
/// 生产应替换为 OpenAI 兼容 / vLLM / Ollama 调用。
/// </summary>
public sealed class StubLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var hits = request.Context
            .Where(c => c.Text.Contains(request.UserPrompt, StringComparison.OrdinalIgnoreCase) ||
                        request.UserPrompt.Contains("设置"))
            .Take(3)
            .ToList();

        var sb = new StringBuilder("【占位回答】从以下检索上下文中获取：\n");
        for (var i = 0; i < request.Context.Count; i++)
        {
            var c = request.Context[i];
            var hit = hits.Contains(c) ? "[命中] " : "";
            sb.AppendLine($"- [{i}] {hit}{c.Section}: {Truncate(c.Text, 80)}");
        }
        sb.AppendLine("（真实 LLM 生成在此接入；citations 由 CitationBuilder 依据算法返回的索引组 装。）");

        var used = request.Context
            .Select((c, i) => (c, i))
            .Where(x => hits.Contains(x.c))
            .Select(x => x.i)
            .ToArray();
        return Task.FromResult(new LlmResponse { Answer = sb.ToString(), CachedChunkIndexes = used });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}