using System.Collections.Concurrent;
using PdfRagQa.Domain;
using PdfRagQa.Domain.Abstractions;

namespace PdfRagQa.Infrastructure.Storage;

/// <summary>内存版反馈仓库（骨架占位）。</summary>
public sealed class InMemoryFeedbackRepository : IFeedbackRepository
{
    private readonly ConcurrentQueue<QaFeedback> _items = new();

    public Task SaveAsync(QaFeedback feedback, CancellationToken ct = default)
    {
        _items.Enqueue(feedback);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QaFeedback>> QueryAsync(string? conversationId = null, CancellationToken ct = default)
    {
        var result = _items
            .Where(f => conversationId is null || f.ConversationId == conversationId)
            .ToList();
        return Task.FromResult<IReadOnlyList<QaFeedback>>(result);
    }
}