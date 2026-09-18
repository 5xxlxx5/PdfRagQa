namespace PdfRagQa.Infrastructure;

/// <summary>
/// 单个 AI 能力的接入配置。三项能力各自独立，因为实际部署中它们常来自不同厂商
/// （例如生成用 DeepSeek、向量化用阿里百炼），地址与密钥都不相同。
/// </summary>
public sealed class AiEndpointOptions
{
    /// <summary>OpenAI 兼容服务地址，例如 https://api.deepseek.com/v1。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    /// <summary>三项齐全才算配置完成；缺任一项时对应能力会跳过并给出明确原因。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// AI 服务配置（appsettings.json 的 "Ai" 节）。
///
/// 注意：ApiKey 不要写进 appsettings.json（该文件纳入版本控制），
/// 应写入 appsettings.Development.json（已在 .gitignore 中）或用环境变量覆盖。
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>文本生成。DeepSeek 可用 deepseek-flash / deepseek-v4-pro。</summary>
    public AiEndpointOptions Chat { get; init; } = new();

    /// <summary>视觉识别（宣传册链路）。DeepSeek 的 flash 模型支持 Vision。</summary>
    public AiEndpointOptions Vision { get; init; } = new();

    /// <summary>
    /// 向量化。DeepSeek 官方 API 不提供 embeddings 接口，必须单独配置
    /// （阿里百炼、硅基流动、自建 TEI/Ollama 均可，只要兼容 OpenAI 协议）。
    /// </summary>
    public AiEndpointOptions Embedding { get; init; } = new();

    /// <summary>
    /// 向量维度。必须与 chunk.embedding 列的维度一致，否则向量检索不可用——
    /// 该一致性由 DatabaseInitializer 在启动时校验。
    /// </summary>
    public int EmbeddingDimensions { get; init; } = 1024;

    /// <summary>单次向量化请求的最大文本数。百炼 text-embedding-v4 为 10，v3 为 25。</summary>
    public int EmbeddingBatchSize { get; init; } = 10;

    /// <summary>页面渲染 DPI。120 实测单页约 992×1403、400~600KB，文字与参数可清晰辨认。</summary>
    public int RenderDpi { get; init; } = 120;

    public int VisionTimeoutSeconds { get; init; } = 180;

    public int EmbeddingTimeoutSeconds { get; init; } = 120;
}
