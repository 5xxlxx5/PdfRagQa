using System.Data.Common;
using Dapper;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Data;

/// <summary>SQL Server 反馈仓库（FR9 反馈闭环）。</summary>
public sealed class SqlServerFeedbackRepository(DbConfig db) : IFeedbackRepository
{
    public async Task SaveAsync(QaFeedback feedback, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.qa_feedback
                (id, conversation_id, question, answer, is_upvote, rejection_reason, corrected_answer, created_at)
            VALUES
                (@Id, @ConversationId, @Question, @Answer, @IsUpvote, @RejectionReason, @CorrectedAnswer, @CreatedAt);
            """;

        await using var conn = db.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            feedback.Id,
            feedback.ConversationId,
            feedback.Question,
            feedback.Answer,
            feedback.IsUpvote,
            feedback.RejectionReason,
            feedback.CorrectedAnswer,
            feedback.CreatedAt,
        }, transaction: null, commandTimeout: 30).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QaFeedback>> QueryAsync(string? conversationId = null, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM dbo.qa_feedback WHERE 1=1";
        if (conversationId is not null) sql += " AND conversation_id=@ConversationId";
        sql += " ORDER BY created_at DESC;";

        await using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<FeedbackRow>(sql, new { ConversationId = conversationId }).ConfigureAwait(false);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed record FeedbackRow(
        string Id, string? Conversation_id, string Question, string Answer, bool? Is_upvote,
        string? Rejection_reason, string? Corrected_answer, DateTimeOffset Created_at)
    {
        public QaFeedback ToDomain() => new()
        {
            Id = Id,
            ConversationId = Conversation_id ?? string.Empty,
            Question = Question,
            Answer = Answer,
            IsUpvote = Is_upvote,
            RejectionReason = Rejection_reason,
            CorrectedAnswer = Corrected_answer,
            CreatedAt = Created_at,
        };
    }
}