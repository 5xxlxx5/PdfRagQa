using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.LLM;

/// <summary>
/// 文本生成实现：调用 OpenAI 兼容的 /chat/completions。
/// DeepSeek 官方即提供 OpenAI 格式（https://api.deepseek.com/v1），因此无需专门的 SDK。
///
/// 关键设计：把检索到的片段编号注入 Prompt，并要求模型在答案里用 [序号] 标注依据，
/// 再从答案中解析出实际引用的序号。这是让引用可信的前提——
/// 如果只是把全部召回片段当作引用返回，用户无法分辨哪一条真正支撑了答案。
///
/// 未配置或调用失败时不抛异常，而是返回带明确标记的降级答案：
/// 问答接口应当把「模型不可用」如实告诉用户，而不是返回 500 或伪装成正常回答。
/// </summary>
public sealed class OpenAiCompatibleChatClient : ILlmClient
{
    private const string DefaultSystemPrompt =
        "你是工业产品资料的问答助手。只能依据给定的资料片段回答问题，不得引入资料之外的信息。"
        + "回答时必须在依据的句子末尾用 [序号] 标注来源，例如 [0][3]；同一句有多个依据时并列标注。"
        + "如果资料中没有答案，直接回答「资料中未找到相关内容」，不要编造。";

    private const string UnconfiguredPrefix = "【生成模型未配置】";
    private const string FailedPrefix = "【生成模型调用失败】";

    /// <summary>注入 Prompt 的上下文总字符上限，避免长文档召回后把请求撑得过大。</summary>
    private const int MaxContextChars = 20000;

    private static readonly Regex CitationPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);

    private readonly AiOptions _options;
    private readonly HttpClient _http;

    public OpenAiCompatibleChatClient(AiOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(options.VisionTimeoutSeconds, 10)) };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (!_options.Chat.IsConfigured)
        {
            return new LlmResponse
            {
                Answer = $"{UnconfiguredPrefix}未配置生成模型（Ai:Chat:BaseUrl / ApiKey / Model 需同时填写），"
                         + "无法生成回答。检索仍已执行，可先检查检索结果是否符合预期。",
            };
        }

        if (request.Context.Count == 0)
        {
            return new LlmResponse { Answer = "资料中未找到相关内容。" };
        }

        try
        {
            var payload = new
            {
                model = _options.Chat.Model,
                temperature = 0,
                messages = BuildMessages(request),
            };

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.Chat.BaseUrl.TrimEnd('/')}/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Chat.ApiKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new LlmResponse
                {
                    Answer = $"{FailedPrefix}HTTP {(int)response.StatusCode}：{Truncate(body, 300)}",
                };
            }

            using var json = JsonDocument.Parse(body);
            var content = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmResponse { Answer = $"{FailedPrefix}模型返回了空内容。" };
            }

            var answer = content.Trim();
            return new LlmResponse
            {
                Answer = answer,
                UsedContextIndexes = ExtractUsedIndexes(answer, request.Context.Count),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LlmResponse
            {
                Answer = $"{FailedPrefix}{ex.GetType().Name}：{ex.Message}",
            };
        }
    }

    private static object[] BuildMessages(LlmRequest request)
    {
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt ?? DefaultSystemPrompt },
        };

        // 多轮历史（暂未接入会话存储，QuestionService 目前传 null）
        if (request.History is not null)
        {
            foreach (var turn in request.History)
                messages.Add(new { role = turn.Role, content = turn.Content });
        }

        messages.Add(new { role = "user", content = BuildUserPrompt(request) });
        return messages.ToArray();
    }

    private static string BuildUserPrompt(LlmRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("资料片段：");

        var used = 0;
        for (var i = 0; i < request.Context.Count; i++)
        {
            var chunk = request.Context[i];
            var text = chunk.Text;

            if (used >= MaxContextChars)
            {
                sb.AppendLine($"[{i}] （已省略：上下文超出 {MaxContextChars} 字符上限）");
                continue;
            }

            if (used + text.Length > MaxContextChars)
                text = text[..Math.Max(MaxContextChars - used, 0)] + "…";

            used += text.Length;
            sb.AppendLine($"[{i}] （{chunk.DocumentId} 第 {chunk.PageNo} 页）{text}");
        }

        sb.AppendLine();
        sb.AppendLine($"问题：{request.UserPrompt}");
        sb.Append("请依据上述片段回答，并用 [序号] 标注来源。");
        return sb.ToString();
    }

    /// <summary>从答案中解析出模型标注的片段序号，越界序号直接丢弃。</summary>
    private static IReadOnlyList<int> ExtractUsedIndexes(string answer, int contextCount)
    {
        var indexes = new SortedSet<int>();
        foreach (Match match in CitationPattern.Matches(answer))
        {
            if (int.TryParse(match.Groups[1].Value, out var index) && index >= 0 && index < contextCount)
                indexes.Add(index);
        }
        return indexes.ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
