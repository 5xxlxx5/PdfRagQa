using Microsoft.AspNetCore.Mvc;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Api.Controllers;

/// <summary>
/// 检索预览（POST /api/retrieval/preview）：只执行检索，不调用生成模型。
///
/// 存在的理由：问答响应里的 citations 只包含「模型标注引用了的」片段，
/// 一旦生成模型不可用，就无法从问答接口观察检索结果，调参与排障会失去抓手。
/// 本端点把检索结果直接暴露出来，也可用于对比不同检索策略的效果。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class RetrievalController(IHybridRetriever hybridRetriever) : ControllerBase
{
    private const int PreviewLength = 80;

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromBody] RetrievalPreviewRequest request, CancellationToken ct)
    {
        var chunks = await hybridRetriever.RetrieveAsync(new HybridQuery
        {
            Query = request.Query,
            DocumentId = request.DocumentId,
            Version = request.Version,
            Language = request.Language,
            TopK = request.TopK,
        }, ct);

        var items = chunks
            .Select(chunk => new RetrievalPreviewItem(
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.DocumentTitle,
                chunk.Version,
                chunk.PageNo,
                chunk.Section,
                Truncate(chunk.Text)))
            .ToList();

        return Ok(new RetrievalPreviewResponse(request.Query, items.Count, items));
    }

    private static string Truncate(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "…";
    }
}
