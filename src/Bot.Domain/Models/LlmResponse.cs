namespace Bot.Domain.Models;

public record LlmResponse(string Text, string? SafetyNote = null);