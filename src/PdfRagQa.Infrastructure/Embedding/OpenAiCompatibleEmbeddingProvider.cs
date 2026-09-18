using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Embedding;

/// <summary>
/// 向量化实现：调用 OpenAI 兼容的 /embeddings 接口。
/// 阿里百炼、硅基流动、自建 TEI / Ollama 都兼容该协议，因此换厂商只需改配置。
///
/// 未配置时抛 <see cref="InvalidOperationException"/> 并给出明确原因，
/// 由调用方（文档接入）捕获后转为告警——与视觉识别保持一致的失败处理方式。
/// </summary>
public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly AiOptions _options;
    private readonly HttpClient _http;

    public OpenAiCompatibleEmbeddingProvider(AiOptions options)
    {
        _options = options;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(options.EmbeddingTimeoutSeconds, 10)),
        };
    }

    public string ModelName => _options.Embedding.Model;

    public int Dimension => _options.EmbeddingDimensions;

    public async Task<IReadOnlyList<float[]>> EmbedTextsAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var list = texts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (list.Count == 0) return Array.Empty<float[]>();

        if (!_options.Embedding.IsConfigured)
        {
            throw new InvalidOperationException(
                "未配置向量化模型（Ai:Embedding:BaseUrl / ApiKey / Model 需同时填写）。"
                + "注意 DeepSeek 官方 API 不提供 embeddings 接口，需另配一家。");
        }

        var batchSize = Math.Max(_options.EmbeddingBatchSize, 1);
        var results = new List<float[]>(list.Count);

        // 服务端对单次请求的文本数有上限（百炼 v4 为 10），按批切分
        for (var offset = 0; offset < list.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = list.GetRange(offset, Math.Min(batchSize, list.Count - offset));
            results.AddRange(await EmbedBatchAsync(batch, ct).ConfigureAwait(false));
        }

        return results;
    }

    public Task<float[]> EmbedImageAsync(Stream imageStream, CancellationToken ct = default)
        => throw new NotSupportedException(
            "当前向量化实现只支持文本。多模态向量需要图文同空间的模型（CLIP 类），"
            + "且百炼的多模态向量不支持 OpenAI 兼容接口，接入时需单独实现。");

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> batch, CancellationToken ct)
    {
        var payload = new
        {
            model = _options.Embedding.Model,
            input = batch,
            dimensions = _options.EmbeddingDimensions,
            encoding_format = "float",
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.Embedding.BaseUrl.TrimEnd('/')}/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Embedding.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"向量化请求失败：HTTP {(int)response.StatusCode}：{Truncate(body, 300)}");
        }

        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("data", out var data))
            throw new InvalidOperationException($"向量化响应缺少 data 字段：{Truncate(body, 300)}");

        var vectors = new List<float[]>(batch.Count);
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("embedding", out var embedding)) continue;

            var vector = new float[embedding.GetArrayLength()];
            var index = 0;
            foreach (var value in embedding.EnumerateArray())
                vector[index++] = value.GetSingle();

            vectors.Add(vector);
        }

        // 数量不匹配会让向量与 chunk 错位，必须立即失败而不是继续写库
        if (vectors.Count != batch.Count)
        {
            throw new InvalidOperationException(
                $"向量化返回数量不匹配：请求 {batch.Count} 条，返回 {vectors.Count} 条");
        }

        return vectors;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
