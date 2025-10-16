using Bot.Application.Interfaces;
using Bot.Domain;
using Bot.Domain.Models;
using Bot.Shared.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Bot.Infrastructure.Llm;

public class OpenAIChatModel : IChatModel
{
    private readonly HttpClient _httpClient;
    private readonly BotOptions _botOptions;
    private readonly ILogger<OpenAIChatModel> _logger;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenAIChatModel(HttpClient httpClient, IOptions<BotOptions> options, ILogger<OpenAIChatModel> logger)
    {
        _logger = logger;
        _httpClient = httpClient;
        _botOptions = options.Value;
        _httpClient.BaseAddress = new Uri(_botOptions.LlmBaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botOptions.LlmApiKey);
    }

    private sealed record ChatReq(
        string model,
        IEnumerable<object> messages,
        double temperature,
        double top_p,
        double frequency_penalty,
        double presence_penalty
    );


    public async Task<LlmResponse> Complete(IEnumerable<ConversationMessage> messages, CancellationToken ct)
    {
        var payload = new ChatReq
        (
            model: _botOptions.LlmModel,
            messages: messages.Select(m => new { role = ToOpenAiRole(m.Role), content = m.Content }),
            temperature: 1.3,
            top_p: 0.9,
            frequency_penalty: 0.9,
            presence_penalty: 0.5
        );

        var json = JsonSerializer.Serialize(payload, _json);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        _logger.LogInformation("Request {req} was sent", payload);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("LLM HTTP {Status}. Body: {Body}", resp.StatusCode, body);
            return new LlmResponse("Ха, мне вообще фиолетово");
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

    private static string ToOpenAiRole(ConversationRole role) => role switch
    {
        ConversationRole.System => "system",
        ConversationRole.User => "user",
        ConversationRole.Assistant => "assistant",
        _ => "user"
    };
}