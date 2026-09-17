namespace PdfRagQa.Domain;

/// <summary>用户对一条问答的反馈记录（点赞/点踩/人工修正）。</summary>
public sealed class QaFeedback
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public IReadOnlyList<Citation> Citations { get; init; } = [];


    /// <summary>true=点赞，false=点踩，null=无评分。</summary>
    public bool? IsUpvote { get; init; }
    public string? RejectionReason { get; init; }
    public string? CorrectedAnswer { get; init; }
    public bool IsCorrected => !string.IsNullOrWhiteSpace(CorrectedAnswer);
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}