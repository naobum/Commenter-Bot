using Bot.Application.Interfaces;
using Bot.Domain.Models;

namespace Bot.Application.Services;

public class MemoryService
{
    private readonly IMemoryStore _store;

    public MemoryService(IMemoryStore store)
    {
        _store = store;
    }

    public Task AppendPerson(ThreadKey key, string text, CancellationToken cancellationToken)
    {
        return _store.Append(key, ConversationMessage.User(text), cancellationToken);
    }
}