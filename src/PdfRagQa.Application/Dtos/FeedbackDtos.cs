namespace PdfRagQa.Application.Dtos;

/// <summary>用户反馈请求（FR9 反馈闭环）。</summary>
public sealed class FeedbackRequest
{
    public string ConversationId { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;

    /// <summary>true=赞，false=踩。</summary>
    public bool IsUpvote { get; init; }

    /// <summary>点踩原因。</summary>
    public string? RejectionReason { get; init; }

    /// <summary>人工修正后的标准答案（回写为黄金标注）。</summary>
    public string? CorrectedAnswer { get; init; }
}