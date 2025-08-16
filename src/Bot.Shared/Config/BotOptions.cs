namespace Bot.Shared.Config;

public class BotOptions
{
    public required string TelegramToken { get; init; }
    public required string WebhookSecretPathSegment { get; init; } // e.g. random 32+ chars
    public string? AllowedChatIdsCsv { get; init; }

    // LLM
    public string LlmBaseUrl { get; init; } = "https://api.openai.com";
    public required string LlmApiKey { get; init; }
    public string LlmModel { get; init; } = "gpt-4o-mini";
    public int MaxContextMessages { get; init; } = 20;
    public double ReplyProbability { get; init; } = 0.2;
}