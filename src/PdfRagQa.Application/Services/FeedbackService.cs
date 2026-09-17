using PdfRagQa.Application.Dtos;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Application.Services;

/// <summary>用户反馈用例（FR9 反馈闭环）。</summary>
public sealed class FeedbackService(IFeedbackRepository repository)
{
    public async Task SubmitAsync(FeedbackRequest request, CancellationToken ct = default)
    {
        var feedback = new QaFeedback
        {
            ConversationId = request.ConversationId,
            Question = request.Question,
            Answer = request.Answer,
            IsUpvote = request.IsUpvote,
            RejectionReason = request.RejectionReason,
            CorrectedAnswer = request.CorrectedAnswer,
        };
        await repository.SaveAsync(feedback, ct);
    }
}