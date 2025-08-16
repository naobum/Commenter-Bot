using Bot.Shared.Config;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bot.Infrastructure.Telegram;

public static class TelegramClientFactory
{
    public static ITelegramBotClient Create(IOptions<BotOptions> options)
    {
        return new TelegramBotClient(options.Value.TelegramToken);
    }
}