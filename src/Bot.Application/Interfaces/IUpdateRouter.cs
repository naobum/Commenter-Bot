using Telegram.Bot.Types;

namespace Bot.Application.Interfaces;

public interface IUpdateRouter
{
    Task Handle(Update update, CancellationToken cancellationToken);
}