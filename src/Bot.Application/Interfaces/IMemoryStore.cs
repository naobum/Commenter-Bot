using Bot.Domain.Models;

namespace Bot.Application.Interfaces;

public interface IMemoryStore
{
    Task Append(ThreadKey threadKey, ConversationMessage message, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMessage>> LoadRecent(ThreadKey threadKey, int maxItems, CancellationToken cancellationToken);
    Task UpsertSummary(ThreadKey threadKey, string Summary, CancellationToken cancellationToken);
    Task<string?> GetSummary(ThreadKey threadKey, CancellationToken cancellationToken);
}