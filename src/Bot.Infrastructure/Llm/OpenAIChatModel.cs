using Bot.Application.Interfaces;
using Bot.Domain;
using Bot.Domain.Models;
using Bot.Shared.Config;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Bot.Infrastructure.Llm;

public class OpenAIChatModel : IChatModel
{
    private readonly HttpClient _httpClient;
    private readonly BotOptions _botOptions;

    public OpenAIChatModel(HttpClient httpClient, IOptions<BotOptions> options)
    {
        _httpClient = httpClient;
        _botOptions = options.Value;
        _httpClient.BaseAddress = new Uri(_botOptions.LlmBaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botOptions.LlmApiKey);
    }

    private sealed record ChatReq(string model, IEnumerable<object> messages, double temperature = 0.7);
    private sealed record ChatMsg(string role, string content);
    private sealed record ChatResp(Choice[] choices);
    private sealed record Choice(Message message);
    private sealed record Message(string role, string content);

    public async Task<LlmResponse> Complete(IEnumerable<ConversationMessage> messages, CancellationToken cancellationToken)
    {
        var payload = new ChatReq(
            model: _botOptions.LlmModel,
            messages: messages.Select(m => new ChatMsg(m.Role switch
            {
                ConversationRole.System => "system",
                ConversationRole.Assistant => "assistant",
                _ => "user"
            }, m.Content))
         );

        var json = JsonSerializer.Serialize(payload);
        using var resp = await _httpClient.PostAsync(
            json,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<ChatResp>(body) ?? throw new Exception("Invalid LLM response");
        var text = parsed.choices.FirstOrDefault()?.message.content ?? string.Empty;

        return new LlmResponse(text);
    }
}