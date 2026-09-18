using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Vision;

/// <summary>
/// 视觉识别实现：调用 OpenAI 兼容的多模态接口（可指向内网私有化部署的 VLM）。
///
/// 宣传手册链路的内容完全来自模型输出，所以提示词强制两件事：
///   1) 逐字转录，保留型号 / 参数 / 单位 / 编号的原始写法，表格用 Markdown 还原；
///   2) 看不清就写「无法辨认」，禁止推测补全——宁可缺，不可错。
///
/// 未配置 BaseUrl / ApiKey / VisionModel 时不会抛异常，而是返回 Succeeded=false 与明确原因，
/// 让导入流程继续并把这个原因作为告警返回给管理员。
/// </summary>
public sealed class OpenAiCompatibleVisionExtractor : IVisionExtractor
{
    private const string SystemPrompt =
        "你是工业产品资料的版面识别助手。你的任务是对给定的 PDF 页面图像做忠实转录与视觉描述。"
        + "严禁推测、补全或改写任何看不清的内容——看不清就明确标注「无法辨认」。";

    private const string UserPrompt =
        "请识别这一页，用 Markdown 输出两部分：\n"
        + "## 文字内容\n"
        + "逐字转录页面上的全部文字，保留型号、参数、编号、单位的原始写法；表格用 Markdown 表格还原。\n"
        + "## 视觉描述\n"
        + "描述页面上的图片、图表、示意图各自表达了什么信息，以及版面的组织方式（如卖点区块、产品渲染图、对比图）。\n"
        + "任何看不清的内容请写「无法辨认」，不要猜测。";

    private readonly AiOptions _options;
    private readonly HttpClient _http;

    public OpenAiCompatibleVisionExtractor(AiOptions options)
    {
        _options = options;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(options.VisionTimeoutSeconds, 10)) };
    }

    public async Task<VisionResult> RecognizeAsync(VisionRequest request, CancellationToken ct = default)
    {
        if (!_options.VisionConfigured)
        {
            return new VisionResult(
                request.PageNo,
                Succeeded: false,
                Content: string.Empty,
                Error: "未配置视觉模型（Ai:BaseUrl / Ai:ApiKey / Ai:VisionModel 需同时填写），已跳过识别");
        }

        try
        {
            var payload = new
            {
                model = _options.VisionModel,
                temperature = 0,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = UserPrompt },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/png;base64,{Convert.ToBase64String(request.PngBytes)}",
                                },
                            },
                        },
                    },
                },
            };

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new VisionResult(
                    request.PageNo,
                    Succeeded: false,
                    Content: string.Empty,
                    Error: $"HTTP {(int)response.StatusCode}：{Truncate(body, 200)}");
            }

            using var json = JsonDocument.Parse(body);
            var content = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return new VisionResult(
                    request.PageNo,
                    Succeeded: false,
                    Content: string.Empty,
                    Error: "模型返回了空内容");
            }

            return new VisionResult(request.PageNo, Succeeded: true, Content: content.Trim(), Error: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VisionResult(
                request.PageNo,
                Succeeded: false,
                Content: string.Empty,
                Error: $"{ex.GetType().Name}：{ex.Message}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
