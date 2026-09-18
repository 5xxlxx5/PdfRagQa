using Microsoft.AspNetCore.Mvc;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Infrastructure.Imports;

namespace PdfRagQa.Api.Controllers;

/// <summary>文档接入：POST /api/documents/import 提交后台导入，GET /api/documents/import/{id} 轮询进度。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class DocumentsController(ImportJobRunner runner) : ControllerBase
{
    [HttpPost("import")]
    public IActionResult Import([FromBody] ImportDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return BadRequest("FilePath 不能为空。");

        var job = runner.Start(request);
        return AcceptedAtAction(nameof(Status), new { id = job.JobId }, job.ToStatus());
    }

    [HttpGet("import/{id}")]
    public IActionResult Status(string id)
    {
        var status = runner.GetStatus(id);
        return status is null ? NotFound() : Ok(status);
    }
}