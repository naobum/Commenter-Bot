using Telegram.Bot.Types;

namespace Bot.Domain.Models;

public static class TelegramExtensions
{
    public static bool IsFromPerson(this Message message)
        => message.From is not null && message.From.IsBot == false && message.Text is not null;
}