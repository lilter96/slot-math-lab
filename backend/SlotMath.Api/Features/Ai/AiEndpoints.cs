using System.Text;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Ai;

/// <summary>
/// POST /api/ai/generate-graph — natural-language → GraphConfig (G26).
///
/// Uses the Anthropic Messages API directly (no SDK dependency).
/// The API key is read server-side only from ANTHROPIC__APIKEY — it never
/// reaches the client bundle.
/// </summary>
public static class AiEndpoints
{
    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-sonnet-4-6";

    // ── System prompt ──────────────────────────────────────────────────────
    private static readonly string SystemPrompt = """
        You are an expert iGaming slot-math designer. Your job is to generate a valid
        SlotMath graph configuration JSON object that describes the requested slot game.

        OUTPUT RULES (CRITICAL):
        - Output ONLY a single, valid JSON object — no markdown fences, no prose, no explanation.
        - The JSON must conform exactly to the GraphConfig schema described below.
        - Never invent or omit required fields.

        ═══════════════════════════════════════════════════════════
        GraphConfig SCHEMA (all fields optional unless marked required)
        ═══════════════════════════════════════════════════════════

        {
          "schemaVersion": "1.0.0",                          // REQUIRED string
          "name": "Game Name",                               // string
          "description": "Optional description",            // string
          "symbols": [                                       // Symbol[]
            { "id": "S1", "name": "Cherry", "kind": "Standard" },
            // kind: Standard | Wild | Scatter | Bonus | Multiplier | Money | Jackpot
          ],
          "paytables": [                                     // Paytable[]
            {
              "id": "main",
              "entries": [
                {
                  "symbolId": "S1",
                  "counts": [3, 4, 5],
                  "payouts": ["5", "10", "25"]
                }
              ]
            }
          ],
          "paylineSets": [                                   // PaylineSet[]
            {
              "id": "lines20",
              "paylines": [
                { "positions": [1, 1, 1, 1, 1] },           // row per column (0-indexed)
                { "positions": [0, 0, 0, 0, 0] }
              ]
            }
          ],
          "reelStrips": [                                    // ReelStrip[]
            { "id": "r1", "name": "Reel 1", "symbols": ["S1","S2","S3","W1","S2","S3","S1","SC1"] }
          ],
          "reelSets": [                                      // ReelSet[]
            { "id": "base", "name": "Base", "stripIds": ["r1","r2","r3","r4","r5"] }
          ],
          "boardConfig": { "rows": 3, "columns": 5 },       // BoardConfig
          "nodes": [                                         // Node[] — REQUIRED (at least Draw + Sink)
            // Each node has "nodeType" as the FIRST property (polymorphic discriminator)
            { "nodeType": "draw", "id": "draw1", "label": "Spin" },
            { "nodeType": "library", "id": "eval1", "label": "Lines", "mechanicName": "lines",
              "parameters": { "paytableId": "main", "paylineSetId": "lines20" } },
            { "nodeType": "metricsSink", "id": "sink1", "label": "Sink" }
          ],
          "edges": [                                         // Edge[]
            { "id": "e1", "sourceNodeId": "draw1", "sourcePort": "out",
              "targetNodeId": "eval1", "targetPort": "in" },
            { "id": "e2", "sourceNodeId": "eval1", "sourcePort": "out",
              "targetNodeId": "sink1", "targetPort": "in" }
          ]
        }

        ═══════════════════════════════════════════════════════════
        NODE TYPES
        ═══════════════════════════════════════════════════════════

        Primitive nodes (nodeType values):
          "draw"        — weighted spin / reel draw. Uses reelSets for board, or drawWeights for
                          inline outcomes: [{ "outcomeId": "A", "weight": 5, "value": 10 }].
          "getState"    — reads state key. Fields: stateKey (string).
          "putState"    — writes state key. Fields: stateKey (string).
          "modifyState" — modifies state via expression. Fields: expressionId (string), outputKey (string).
          "loop"        — fixpoint. Fields: maxIterations (int), stopConditionId (string expr id).
          "branch"      — conditional fork. Fields: conditionId (string expr id).
                          Outputs ports: "true" and "false".
          "map"         — transform. Fields: transformId (string, e.g. "lines", "scatter").
          "metricsSink" — exactly ONE required; terminal. No outputs.

        Catalog / library node (nodeType "library"):
          "library"     — catalog mechanic. Fields: mechanicName (one of: "lines", "ways",
                          "scatter", "cascade", "sticky-wild", "hold-and-win"),
                          parameters: { "paytableId": "...", "paylineSetId": "..." }

        ═══════════════════════════════════════════════════════════
        GRAPH RULES (compiler will reject violations)
        ═══════════════════════════════════════════════════════════
        1. Exactly ONE "metricsSink" node.
        2. No directed cycles except through a "loop" node's body port.
        3. Every edge's sourceNodeId and targetNodeId must match an existing node id.
        4. The graph must have at least one entry node (a node with no incoming edges).
        5. "nodeType" must be the FIRST key of every node object.
        6. "library" nodes reference catalog mechanics (lines/ways/scatter/cascade/etc.).
        7. For a reel-based draw: include reelStrips + reelSets + boardConfig.
        8. For a ways game use "ways" library node (no paylineSets needed).

        ═══════════════════════════════════════════════════════════
        MINIMAL VALID EXAMPLE (3-reel, 5-symbol, 5-payline game)
        ═══════════════════════════════════════════════════════════

        {
          "schemaVersion": "1.0.0",
          "name": "Classic Fruit",
          "symbols": [
            {"id":"S1","name":"Cherry","kind":"Standard"},
            {"id":"S2","name":"Lemon","kind":"Standard"},
            {"id":"S3","name":"Bell","kind":"Standard"},
            {"id":"W1","name":"Wild","kind":"Wild"}
          ],
          "paytables": [{
            "id": "pt1",
            "entries": [
              {"symbolId":"S1","counts":[3],"payouts":["5"]},
              {"symbolId":"S2","counts":[3],"payouts":["3"]},
              {"symbolId":"S3","counts":[3],"payouts":["8"]},
              {"symbolId":"W1","counts":[3],"payouts":["15"]}
            ]
          }],
          "paylineSets": [{
            "id": "pl1",
            "paylines": [
              {"positions":[1,1,1]},
              {"positions":[0,0,0]},
              {"positions":[2,2,2]},
              {"positions":[0,1,2]},
              {"positions":[2,1,0]}
            ]
          }],
          "reelStrips": [
            {"id":"r1","name":"Reel1","symbols":["S1","S2","W1","S3","S2","S1","S3"]},
            {"id":"r2","name":"Reel2","symbols":["S2","S1","S3","W1","S2","S3","S1"]},
            {"id":"r3","name":"Reel3","symbols":["S3","W1","S1","S2","S3","S1","S2"]}
          ],
          "reelSets": [{"id":"rs1","name":"Base","stripIds":["r1","r2","r3"]}],
          "boardConfig": {"rows":3,"columns":3},
          "nodes": [
            {"nodeType":"draw","id":"d1","label":"Spin"},
            {"nodeType":"library","id":"ev1","label":"Lines","mechanicName":"lines",
             "parameters":{"paytableId":"pt1","paylineSetId":"pl1"}},
            {"nodeType":"metricsSink","id":"sk1","label":"Sink"}
          ],
          "edges": [
            {"id":"e1","sourceNodeId":"d1","sourcePort":"out","targetNodeId":"ev1","targetPort":"in"},
            {"id":"e2","sourceNodeId":"ev1","sourcePort":"out","targetNodeId":"sk1","targetPort":"in"}
          ]
        }
        """;

    public static RouteGroupBuilder MapAi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/generate-graph", async (
            GenerateGraphRequest request,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory) =>
        {
            // ── Key guard ──────────────────────────────────────────────────
            var apiKey = configuration["ANTHROPIC__APIKEY"]
                         ?? Environment.GetEnvironmentVariable("ANTHROPIC__APIKEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Results.Problem(
                    detail: "The AI service is not configured on this server. Set the ANTHROPIC__APIKEY environment variable.",
                    statusCode: 503,
                    title: "AI service not configured");
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest(new { error = "Prompt must not be empty." });

            var model = configuration["AI__Model"] ?? DefaultModel;
            var client = httpClientFactory.CreateClient("anthropic");

            // ── First attempt ──────────────────────────────────────────────
            var (config, errors, parseError) = await CallClaudeAndCompile(
                client, apiKey, model,
                userMessage: request.Prompt);

            if (config is not null)
                return Results.Ok(config);

            // ── One repair pass on validation failure ──────────────────────
            if (errors is not null && errors.Count > 0)
            {
                var repairMessage = BuildRepairMessage(request.Prompt, errors);
                var (config2, errors2, parseError2) = await CallClaudeAndCompile(
                    client, apiKey, model,
                    userMessage: repairMessage);

                if (config2 is not null)
                    return Results.Ok(config2);

                var finalErrors = errors2 ?? errors;
                return Results.UnprocessableEntity(new GenerateGraphErrorResponse
                {
                    Error = "The generated graph failed compiler validation after one repair attempt.",
                    CompilerErrors = finalErrors.Select(e => new AiCompilerError
                    {
                        NodeId = e.NodeId,
                        Code = e.Code,
                        Message = e.Message,
                    }).ToList(),
                });
            }

            // Parse error (model returned invalid JSON)
            return Results.UnprocessableEntity(new GenerateGraphErrorResponse
            {
                Error = parseError ?? "Failed to parse the model response as a valid GraphConfig.",
                CompilerErrors = Array.Empty<AiCompilerError>(),
            });
        });

        return group;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static async Task<(object? Config, IReadOnlyList<CompileError>? Errors, string? ParseError)>
        CallClaudeAndCompile(
            HttpClient client,
            string apiKey,
            string model,
            string userMessage)
    {
        string rawJson;
        try
        {
            rawJson = await CallClaude(client, apiKey, model, userMessage);
        }
        catch (Exception ex)
        {
            return (null, null, $"AI API call failed: {ex.Message}");
        }

        // Strip possible markdown fences the model may emit despite instructions
        rawJson = StripMarkdownFences(rawJson);

        // Parse
        GraphConfig graphConfig;
        try
        {
            graphConfig = JsonSerializer.Deserialize<GraphConfig>(rawJson, SlotMath.Core.JsonOptions.Default)!;
        }
        catch (Exception ex)
        {
            return (null, null, $"Could not parse model output as GraphConfig JSON: {ex.Message}");
        }

        if (graphConfig is null)
            return (null, null, "Model returned null or empty JSON.");

        // Compile / validate
        var compiler = new GraphCompiler();
        var result = compiler.Compile(graphConfig);

        if (result.IsValid)
            return (graphConfig, null, null);

        return (null, result.Errors, null);
    }

    private static async Task<string> CallClaude(
        HttpClient client,
        string apiKey,
        string model,
        string userMessage)
    {
        var requestBody = new
        {
            model,
            max_tokens = 4096,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, SlotMath.Core.JsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AnthropicApiUrl)
        {
            Content = content,
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await client.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Anthropic API returned {(int)response.StatusCode}: {errBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return text ?? string.Empty;
    }

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text[(firstNewline + 1)..];
            if (text.EndsWith("```", StringComparison.Ordinal))
                text = text[..^3].TrimEnd();
        }
        return text.Trim();
    }

    private static string BuildRepairMessage(
        string originalPrompt,
        IReadOnlyList<CompileError> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The previously generated GraphConfig failed compiler validation.");
        sb.AppendLine();
        sb.AppendLine("Validation errors:");
        foreach (var e in errors)
        {
            sb.Append($"  [{e.Code}]");
            if (!string.IsNullOrEmpty(e.NodeId)) sb.Append($" (node {e.NodeId})");
            sb.AppendLine($": {e.Message}");
        }
        sb.AppendLine();
        sb.AppendLine("Please fix all errors and regenerate the complete GraphConfig JSON.");
        sb.AppendLine("Output ONLY the corrected JSON object — no explanation, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine($"Original game description: {originalPrompt}");
        return sb.ToString();
    }
}

// ── Request / response models ──────────────────────────────────────────────

public sealed record GenerateGraphRequest
{
    public required string Prompt { get; init; }
}

public sealed record GenerateGraphErrorResponse
{
    public required string Error { get; init; }
    public required IReadOnlyList<AiCompilerError> CompilerErrors { get; init; }
}

public sealed record AiCompilerError
{
    public string? NodeId { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
}
