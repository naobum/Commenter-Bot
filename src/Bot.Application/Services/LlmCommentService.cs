using Bot.Application.Interfaces;
using Bot.Application.Prompt;
using Bot.Domain.Models;

namespace Bot.Application.Services;

public sealed class LlmCommentService
{
    private readonly IChatModel _chat;
    private readonly IMemoryStore _memory;
    private readonly int _maxContext;

    public LlmCommentService(IChatModel chat, IMemoryStore memory, int maxContext)
    {
        _chat = chat;
        _memory = memory;
        _maxContext = Math.Max(4, maxContext);
    }

    public async Task<string> BuildReply(ThreadKey key, string userText, CancellationToken cancellationToken)
    {
        var history = await _memory.LoadRecent(key, _maxContext, cancellationToken);
        var summary = await _memory.GetSummary(key, cancellationToken);

        var messages = new List<ConversationMessage>
        {
            ConversationMessage.System(CommentPromt.SystemPromt),
        };

        if (!string.IsNullOrWhiteSpace(summary))
            messages.Add(ConversationMessage.System($"Краткое резюме треда: {summary}"));

        messages.AddRange(history);
        messages.Add(ConversationMessage.User(userText));

        var resp = await _chat.Complete(messages, cancellationToken);

        var assistant = ConversationMessage.Assistant(resp.Text);
        await _memory.Append(key, assistant, cancellationToken);

        if (history.Count >= _maxContext)
        {
            var summMsgs = new List<ConversationMessage>
            {
                ConversationMessage.System("Суммируй разговор в 1–2 предложениях, сохранив факты и ники без приватных данных."),
            };
            summMsgs.AddRange(history);
            var s = await _chat.Complete(summMsgs, cancellationToken);
            await _memory.UpsertSummary(key, s.Text, cancellationToken);
        }

        return resp.Text.Trim();
    }
}