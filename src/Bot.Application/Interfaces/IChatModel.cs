using Bot.Domain.Models;

namespace Bot.Application.Interfaces;

public interface IChatModel
{
    Task<LlmResponse> Complete(
        IEnumerable<ConversationMessage> messages,
        CancellationToken cancellationToken = default);
}