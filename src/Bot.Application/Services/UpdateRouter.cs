using Bot.Application.Interfaces;
using Bot.Domain.Models;
using Bot.Shared.Config;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.Application.Services;

public class UpdateRouter : IUpdateRouter
{
    private readonly ITelegramBotClient _bot;
    private readonly LlmCommentService _llm;
    private readonly MemoryService _memory;
    private readonly BotOptions _options;
    private readonly ILogger<UpdateRouter> _logger;

    private readonly HashSet<long> _allowedChats;

    public UpdateRouter(ITelegramBotClient bot, LlmCommentService llm, MemoryService memory, BotOptions options, ILogger<UpdateRouter> logger)
    {
        _bot = bot;
        _llm = llm;
        _memory = memory;
        _options = options;
        _allowedChats = ParseAllowed(options.AllowedChatIdsCsv);
        _logger = logger;
    }

    public async Task Handle(Update update, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Update: {Type}", update.Type);

        switch (update.Type)
        {
            case UpdateType.Message:
                if (update.Message is { } m)
                {
                    _logger.LogInformation("Message chat={chat} type={ctype} thread={thread} autoFwd={af} fromBot={bot} textLen={len}",
                        m.Chat?.Id, m.Chat?.Type, m.MessageThreadId, m.IsAutomaticForward, m.From?.IsBot, m.Text?.Length ?? (m.Caption?.Length ?? 0));
                    await OnMessage(m, cancellationToken);
                }
                break;
            default:
                break;
        }
    }

    private async Task OnMessage(Message message, CancellationToken cancelationToken)
    {
        // только группы/супергруппы
        if (message.Chat.Type is not ChatType.Supergroup && message.Chat.Type is not ChatType.Group) return;

        if (_allowedChats.Count > 0 && !_allowedChats.Contains(message.Chat.Id)) return;

        var threadId = message.MessageThreadId;
        var rootId = message.ReplyToMessage?.MessageId ?? message.MessageId;
        var keyId = threadId ?? rootId;
        var key = new ThreadKey(message.Chat.Id, keyId);

        if (message.IsAutomaticForward == true)
        {
            var textForLlm = message.Text ?? message.Caption;
            if (string.IsNullOrWhiteSpace(textForLlm))
                textForLlm = "Описание поста отсутствует. Дай короткий уместный комментарий к медиа.";

            var reply = await _llm.BuildReply(key, textForLlm, cancelationToken);

            await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: reply,
                messageThreadId: threadId,
                replyParameters: message.MessageId,
                cancellationToken: cancelationToken);

            return;
        }

        if (message.IsFromPerson())
        {
            var userText = (message.Text ?? message.Caption ?? "").Trim();
            if (userText.StartsWith("/") || userText.Length < 2) return;

            var reply = await _llm.BuildReply(key, userText, cancelationToken);

            bool isReplyToBot = message.ReplyToMessage?.From?.Id == _bot.BotId;
            if (!isReplyToBot) return;

            await _bot.SendMessage(
                chatId: message.Chat.Id,
                text: reply,
                messageThreadId: threadId,
                replyParameters: message.MessageId,
                cancellationToken: cancelationToken);
        }
    }
    private static HashSet<long> ParseAllowed(string? csv)
    {
        var set = new HashSet<long>();
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (long.TryParse(part, out var id)) set.Add(id);
        return set;
    }
}