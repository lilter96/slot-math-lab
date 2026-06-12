using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SlotMath.Api.Features.Ai;

namespace SlotMath.Api.Tests.Ai;

// ══════════════════════════════════════════════════════════════════════════════
//  G28 lint rule tests
//
//  Each lint rule must fire on a crafted failing config and stay silent on
//  a clean one (per the DoD).
// ══════════════════════════════════════════════════════════════════════════════

[Collection("SerialTests")]
public class LintEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public LintEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object BuildConfig(
        object[]? symbols = null,
        object[]? paytables = null,
        object[]? nodes = null)
    {
        return new
        {
            schemaVersion = "1.0.0",
            symbols = symbols ?? Array.Empty<object>(),
            paytables = paytables ?? Array.Empty<object>(),
            nodes = nodes ?? new object[]
            {
                new { nodeType = "metricsSink", id = "sink-1", label = "Sink", inputs = new { }, outputs = new { } },
            },
            edges = Array.Empty<object>(),
        };
    }

    private async Task<LintResponse> PostLintAsync(object config, double? rtp = null)
    {
        var body = new { config, rtp };
        var response = await _client.PostAsJsonAsync("/api/ai/lint", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LintResponse>(json, _jsonOptions)!;
    }

    // ── Rule: rtp-out-of-band ─────────────────────────────────────────────────

    [Fact]
    public async Task RtpOutOfBand_Rule_FiresWhenRtpTooLow()
    {
        var config = BuildConfig();
        var result = await PostLintAsync(config, rtp: 0.60);

        Assert.Contains(result.Issues, i => i.RuleId == "rtp-out-of-band" && i.Severity == "error");
    }

    [Fact]
    public async Task RtpOutOfBand_Rule_FiresWhenRtpAbove1()
    {
        var config = BuildConfig();
        var result = await PostLintAsync(config, rtp: 1.10);

        Assert.Contains(result.Issues, i => i.RuleId == "rtp-out-of-band" && i.Severity == "error");
    }

    [Fact]
    public async Task RtpOutOfBand_Rule_SilentWhenRtpInRange()
    {
        var config = BuildConfig();
        var result = await PostLintAsync(config, rtp: 0.96);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "rtp-out-of-band");
    }

    [Fact]
    public async Task RtpOutOfBand_Rule_SilentWhenRtpNotProvided()
    {
        var config = BuildConfig();
        var result = await PostLintAsync(config); // no rtp

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "rtp-out-of-band");
    }

    // ── Rule: missing-sink ────────────────────────────────────────────────────

    [Fact]
    public async Task MissingSink_Rule_FiresWhenNoSinkNode()
    {
        // Config with a draw node but no sink
        var config = new
        {
            schemaVersion = "1.0.0",
            symbols = Array.Empty<object>(),
            paytables = Array.Empty<object>(),
            nodes = new object[]
            {
                new
                {
                    nodeType = "draw",
                    id = "draw-1",
                    label = "Draw",
                    drawWeights = new object[]
                    {
                        new { outcomeId = "A", weight = 10, value = 5 },
                    },
                    inputs = new { },
                    outputs = new { },
                },
            },
            edges = Array.Empty<object>(),
        };

        var result = await PostLintAsync(config);

        Assert.Contains(result.Issues, i => i.RuleId == "missing-sink" && i.Severity == "error");
    }

    [Fact]
    public async Task MissingSink_Rule_SilentWhenSinkPresent()
    {
        var config = BuildConfig(); // default config includes a sink
        var result = await PostLintAsync(config);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "missing-sink");
    }

    // ── Rule: dead-symbol ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeadSymbol_Rule_FiresWhenSymbolInPaytableButNotInWeights()
    {
        // Symbol "sym-z" is in the paytable but not in any DrawWeight or ReelStrip
        var config = BuildConfig(
            symbols: new object[]
            {
                new { id = "sym-a", name = "A", kind = "Standard" },
                new { id = "sym-z", name = "ZombieSymbol", kind = "Standard" },
            },
            paytables: new object[]
            {
                new
                {
                    id = "pt",
                    entries = new object[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3 }, payouts = new[] { "10" } },
                        new { symbolId = "sym-z", counts = new[] { 3 }, payouts = new[] { "999" } },
                    }
                }
            },
            nodes: new object[]
            {
                new
                {
                    nodeType = "draw",
                    id = "draw-1",
                    label = "Draw",
                    drawWeights = new object[]
                    {
                        // sym-z NOT present here
                        new { outcomeId = "sym-a", weight = 10, value = 10 },
                    },
                    inputs = new { },
                    outputs = new { },
                },
                new { nodeType = "metricsSink", id = "sink-1", label = "Sink", inputs = new { }, outputs = new { } },
            }
        );

        var result = await PostLintAsync(config);

        Assert.Contains(result.Issues, i => i.RuleId == "dead-symbol" && i.Severity == "warning");
    }

    [Fact]
    public async Task DeadSymbol_Rule_SilentWhenAllSymbolsReferenced()
    {
        var config = BuildConfig(
            symbols: new object[]
            {
                new { id = "sym-a", name = "A", kind = "Standard" },
            },
            paytables: new object[]
            {
                new
                {
                    id = "pt",
                    entries = new object[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3 }, payouts = new[] { "10" } },
                    }
                }
            },
            nodes: new object[]
            {
                new
                {
                    nodeType = "draw",
                    id = "draw-1",
                    label = "Draw",
                    drawWeights = new object[]
                    {
                        new { outcomeId = "sym-a", weight = 10, value = 10 },
                    },
                    inputs = new { },
                    outputs = new { },
                },
                new { nodeType = "metricsSink", id = "sink-1", label = "Sink", inputs = new { }, outputs = new { } },
            }
        );

        var result = await PostLintAsync(config);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "dead-symbol");
    }

    // ── Rule: paytable-anomaly ────────────────────────────────────────────────

    [Fact]
    public async Task PaytableAnomaly_Rule_FiresWhenAllPayoutsZero()
    {
        var config = BuildConfig(
            paytables: new object[]
            {
                new
                {
                    id = "pt",
                    entries = new object[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3, 4, 5 }, payouts = new[] { "0", "0", "0" } },
                    }
                }
            }
        );

        var result = await PostLintAsync(config);

        Assert.Contains(result.Issues, i => i.RuleId == "paytable-anomaly" && i.Severity == "warning");
    }

    [Fact]
    public async Task PaytableAnomaly_Rule_SilentWhenAtLeastOnePayoutNonZero()
    {
        var config = BuildConfig(
            paytables: new object[]
            {
                new
                {
                    id = "pt",
                    entries = new object[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3, 4, 5 }, payouts = new[] { "0", "10", "50" } },
                    }
                }
            }
        );

        var result = await PostLintAsync(config);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == "paytable-anomaly");
    }

    // ── Clean config produces no errors ──────────────────────────────────────

    [Fact]
    public async Task CleanConfig_ProducesNoErrors_AtNominalRtp()
    {
        var config = BuildConfig(
            symbols: new object[]
            {
                new { id = "sym-a", name = "A", kind = "Standard" },
            },
            paytables: new object[]
            {
                new
                {
                    id = "pt",
                    entries = new object[]
                    {
                        new { symbolId = "sym-a", counts = new[] { 3 }, payouts = new[] { "10" } },
                    }
                }
            }
        );

        var result = await PostLintAsync(config, rtp: 0.96);

        // No errors (warnings for dead-symbol etc. may still appear on this minimal config,
        // but there must be no error-severity issues)
        Assert.DoesNotContain(result.Issues, i => i.Severity == "error");
    }
}
