using Microsoft.AspNetCore.Mvc;
using PdfRagQa.Application.Dtos;
using PdfRagQa.Application.Services;

namespace PdfRagQa.Api.Controllers;

/// <summary>用户反馈（POST /api/feedback）。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class FeedbackController(FeedbackService feedbackService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] FeedbackRequest request, CancellationToken ct)
    {
        await feedbackService.SubmitAsync(request, ct);
        return Ok();
    }
}