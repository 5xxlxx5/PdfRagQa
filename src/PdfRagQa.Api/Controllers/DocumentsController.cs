using Microsoft.AspNetCore.Mvc;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Application.Services;

namespace PdfRagQa.Api.Controllers;

/// <summary>文档接入（POST /api/documents/import）。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class DocumentsController(DocumentIngestionService ingestionService) : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportDocumentRequest request, CancellationToken ct)
    {
        var result = await ingestionService.ImportAsync(request, ct);
        return Ok(result);
    }
}