namespace PdfRagQa.Infrastructure;

/// <summary>
/// AI 服务配置（appsettings.json 的 "Ai" 节）。
/// 视觉模型走 OpenAI 兼容协议，可指向内网私有化部署的 VLM 服务。
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>OpenAI 兼容服务地址，例如 https://api.openai.com/v1 或内网地址。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>API Key；为空时视觉链路会跳过识别并给出明确错误，而不是抛异常。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>视觉模型名；为空时同样跳过识别。</summary>
    public string VisionModel { get; init; } = string.Empty;

    /// <summary>页面渲染 DPI。120 实测单页约 992×1403、400~600KB，文字与参数可清晰辨认。</summary>
    public int RenderDpi { get; init; } = 120;

    /// <summary>视觉模型调用超时（秒）。</summary>
    public int VisionTimeoutSeconds { get; init; } = 180;

    /// <summary>是否已配置可用的视觉模型。</summary>
    public bool VisionConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(VisionModel);
}
