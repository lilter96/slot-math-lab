using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlotMath.Api.Features.Configs;

namespace SlotMath.Api.Features.Ai;

// ══════════════════════════════════════════════════════════════════════════════
//  G28 — AI explain
//
//  POST /api/ai/explain
//  Uses Claude to explain slot math characteristics.
//  Gracefully degrades when ANTHROPIC__APIKEY is not set.
// ══════════════════════════════════════════════════════════════════════════════

public static class ExplainEndpoints
{
    // Anthropic Messages API endpoint
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicModel = "claude-3-5-haiku-20241022";
    private const string AnthropicVersion = "2023-06-01";

    public static void MapExplain(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/explain", async (ExplainRequest request, IConfiguration configuration) =>
        {
            // Resolve API key — server-side only (invariant #9)
            var apiKey = configuration["ANTHROPIC:APIKEY"]
                ?? configuration["Anthropic:ApiKey"]
                ?? Environment.GetEnvironmentVariable("ANTHROPIC__APIKEY")
                ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Results.Ok(new ExplainResponse
                {
                    Explanation = "AI explain requires ANTHROPIC__APIKEY to be set.",
                });
            }

            // Build a concise factual prompt from the provided metrics only
            var userPrompt = $"""
                Here are the exact math metrics for an iGaming slot game:

                - RTP: {request.Rtp:F4} ({request.Rtp * 100:F2}%)
                - Hit frequency: {request.HitFrequency:F4} ({request.HitFrequency * 100:F2}%)
                - Volatility index: {request.Volatility:F2}
                {(request.Ci95 != null ? $"- 95% CI: {request.Ci95}" : "")}

                Explain what these numbers tell us about the player experience in 3-4 sentences.
                """;

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("x-api-key", apiKey);
                http.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

                var requestBody = new AnthropicMessagesRequest
                {
                    Model = AnthropicModel,
                    MaxTokens = 300,
                    System = "You are a casino math analyst. Given exact metrics from an iGaming slot game, " +
                             "explain in 3-4 sentences what the RTP, hit frequency, and volatility tell us " +
                             "about the player experience. Only cite the numbers given to you. " +
                             "Be concise and precise — no invented figures.",
                    Messages = new[]
                    {
                        new AnthropicMessage { Role = "user", Content = userPrompt },
                    },
                };

                var json = JsonSerializer.Serialize(requestBody, AnthropicJsonContext.Default.AnthropicMessagesRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await http.PostAsync(AnthropicEndpoint, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Ok(new ExplainResponse
                    {
                        Explanation = $"AI explain unavailable: API returned {(int)response.StatusCode}.",
                    });
                }

                var parsed = JsonSerializer.Deserialize<AnthropicMessagesResponse>(
                    responseJson, AnthropicJsonContext.Default.AnthropicMessagesResponse);

                var explanation = parsed?.Content?.FirstOrDefault()?.Text
                    ?? "AI explain returned an empty response.";

                return Results.Ok(new ExplainResponse { Explanation = explanation });
            }
            catch (Exception ex)
            {
                return Results.Ok(new ExplainResponse
                {
                    Explanation = $"AI explain unavailable: {ex.Message}",
                });
            }
        });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record ExplainRequest
{
    public object? Config { get; init; }
    public double Rtp { get; init; }
    public double HitFrequency { get; init; }
    public double Volatility { get; init; }
    public string? Ci95 { get; init; }
}

public sealed record ExplainResponse
{
    public required string Explanation { get; init; }
}

// ── Anthropic API shapes (minimal surface) ────────────────────────────────────

internal sealed record AnthropicMessagesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 300;

    [JsonPropertyName("system")]
    public required string System { get; init; }

    [JsonPropertyName("messages")]
    public required AnthropicMessage[] Messages { get; init; }
}

internal sealed record AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed record AnthropicMessagesResponse
{
    [JsonPropertyName("content")]
    public AnthropicContentBlock[]? Content { get; init; }
}

internal sealed record AnthropicContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

[JsonSerializable(typeof(AnthropicMessagesRequest))]
[JsonSerializable(typeof(AnthropicMessagesResponse))]
[JsonSerializable(typeof(AnthropicMessage))]
[JsonSerializable(typeof(AnthropicContentBlock))]
internal partial class AnthropicJsonContext : JsonSerializerContext { }
