namespace PdfRagQa.Domain.Abstractions;

/// <summary>反馈存储端口：持久化用户点赞/点踩/人工修正。</summary>
public interface IFeedbackRepository
{
    Task SaveAsync(QaFeedback feedback, CancellationToken ct = default);
    Task<IReadOnlyList<QaFeedback>> QueryAsync(string? conversationId = null, CancellationToken ct = default);
}