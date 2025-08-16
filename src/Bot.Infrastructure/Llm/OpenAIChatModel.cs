using Bot.Application.Interfaces;
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
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenAIChatModel(HttpClient httpClient, IOptions<BotOptions> options)
    {
        _httpClient = httpClient;
        _botOptions = options.Value;
        _httpClient.BaseAddress = new Uri(_botOptions.LlmBaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botOptions.LlmApiKey);
    }

    private sealed record ChatReq(string model, IEnumerable<object> messages, double temperature = 0.7);

    public async Task<LlmResponse> Complete(IEnumerable<ConversationMessage> messages, CancellationToken ct)
    {
        var payload = new ChatReq
        (
            model: _botOptions.LlmModel,
            temperature: 0.7,
            messages: messages.Select(m => new { role = m.Role, content = m.Content })
        );

        var json = JsonSerializer.Serialize(payload, _json);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return new LlmResponse("Пока думаю над остроумным ответом 🤔");
        }

        // минимальный парсер
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return new LlmResponse(content ?? "📝");
    }
}