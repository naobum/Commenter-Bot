namespace Bot.Domain.Models;

public record ConversationMessage(ConversationRole Role, string Content, DateTimeOffset Ts)
{
    public static ConversationMessage System(string text) => new(ConversationRole.System, text, DateTimeOffset.UtcNow);
    public static ConversationMessage User(string text) => new(ConversationRole.User, text, DateTimeOffset.UtcNow);
    public static ConversationMessage Assistant(string text) => new(ConversationRole.Assistant, text, DateTimeOffset.UtcNow);
}