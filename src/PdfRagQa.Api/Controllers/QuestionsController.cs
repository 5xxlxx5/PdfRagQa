using Microsoft.AspNetCore.Mvc;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Application.Services;

namespace PdfRagQa.Api.Controllers;

/// <summary>统一问答入口（POST /api/questions）。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class QuestionsController(QuestionService questionService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] QuestionRequest request, CancellationToken ct)
    {
        var response = await questionService.AskAsync(request, ct);
        return Ok(response);
    }
}