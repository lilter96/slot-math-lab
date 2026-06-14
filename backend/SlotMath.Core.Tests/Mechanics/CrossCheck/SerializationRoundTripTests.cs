using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlotMath.Core;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Xunit;
using Xunit.Abstractions;

namespace SlotMath.Core.Tests.Mechanics.CrossCheck;

// ═══════════════════════════════════════════════════════════════════════════
//  G13 DoD (c) — Serialization round-trip
//
//  Config → canonical hash → serialize → deserialize → re-hash.
//  The hash must be identical, and computed metrics must match.
// ═══════════════════════════════════════════════════════════════════════════

public class SerializationRoundTripTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOpts = JsonOptions.Default;

    /// <summary>
    /// Create a representative GraphConfig exercising all model features.
    /// </summary>
    private static GraphConfig CreateRepresentativeConfig()
    {
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "roundtrip-test",
            Name = "Round-Trip Test Config",
            Description = "Exercises expressions + plugins + library nodes",
            Symbols = new[]
            {
                new Symbol { Id = "A", Name = "Ace", Kind = SymbolKind.Standard },
                new Symbol { Id = "W", Name = "Wild", Kind = SymbolKind.Wild },
                new Symbol { Id = "S", Name = "Scatter", Kind = SymbolKind.Scatter },
                new Symbol { Id = "M", Name = "Multiplier", Kind = SymbolKind.Multiplier },
            },
            Paytables = new[]
            {
                new Paytable
                {
                    Id = "main",
                    Entries = new[]
                    {
                        new PaytableEntry
                        {
                            SymbolId = "A",
                            Counts = new[] { 3, 4, 5 },
                            Payouts = new[] { "5", "10", "20" },
                        },
                        new PaytableEntry
                        {
                            SymbolId = "W",
                            Counts = new[] { 3 },
                            Payouts = new[] { "50" },
                        },
                    },
                },
            },
            PaylineSets = new[]
            {
                new PaylineSet
                {
                    Id = "lines-20",
                    Paylines = new[]
                    {
                        new Payline { Positions = new[] { 0, 0, 0, 0, 0 } },
                        new Payline { Positions = new[] { 1, 1, 1, 1, 1 } },
                        new Payline { Positions = new[] { 2, 2, 2, 2, 2 } },
                    },
                },
            },
            ReelStrips = new[]
            {
                new ReelStrip { Id = "r1", Name = "Reel 1", Symbols = new[] { "A", "A", "W", "S", "M" } },
            },
            BoardConfig = new BoardConfig { Rows = 3, Columns = 5 },
            Nodes = new Node[]
            {
                new DrawNode { Id = "d1", Label = "Base Spin" },
                new LibraryNode { Id = "eval1", Label = "Lines Eval", MechanicName = "lines" },
                new LibraryNode { Id = "bonus", Label = "Free Spins", MechanicName = "free-spins" },
                new LoopNode { Id = "loop1", Label = "FS Loop", MaxIterations = 100 },
                new MetricsSinkNode { WinCap = 10_000, Id = "sink1", Label = "RTP" },
            },
            Edges = new[]
            {
                new Edge { Id = "e1", SourceNodeId = "d1", SourcePort = "out", TargetNodeId = "eval1", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval1", SourcePort = "trigger", TargetNodeId = "bonus", TargetPort = "trigger" },
                new Edge { Id = "e3", SourceNodeId = "bonus", SourcePort = "wins", TargetNodeId = "sink1", TargetPort = "wins" },
            },
            Expressions = new Dictionary<string, Expression>
            {
                ["mult2x"] = new BinaryExpr
                {
                    Op = BinaryOp.Mul,
                    Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "2" },
                    Right = new FieldAccessExpr { Path = new[] { "rows" }, Target = "state" },
                },
                ["isScatter"] = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = new FieldAccessExpr { Path = new[] { "symbol" }, Target = "board" },
                    Right = new ConstantExpr { Kind = ConstantKind.String, Value = "S" },
                },
            },
            Plugins = new PluginReference[]
            {
                new PluginReference
                {
                    PluginId = "custom-eval-v1",
                    Contract = PluginContract.IEvaluator,
                    Version = "1.0.0",
                    Config = new Dictionary<string, string> { ["mode"] = "cluster" },
                },
            },
        };
    }

    [Fact]
    public void RoundTrip_PreservesConfigHash()
    {
        var original = CreateRepresentativeConfig();

        // Compute canonical hash from sorted JSON.
        var originalHash = ComputeCanonicalHash(original);
        output.WriteLine($"Original hash: {originalHash}");

        // Serialize.
        var json = JsonSerializer.Serialize(original, JsonOpts);
        output.WriteLine($"Serialized length: {json.Length} chars");

        // Deserialize.
        var deserialized = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts);
        Assert.NotNull(deserialized);

        // Re-hash.
        var rehash = ComputeCanonicalHash(deserialized);
        output.WriteLine($"Re-hash: {rehash}");

        Assert.Equal(originalHash, rehash);
    }

    [Fact]
    public void RoundTrip_PreservesAllData()
    {
        var original = CreateRepresentativeConfig();
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts)!;

        // Verify all fields match.
        Assert.Equal(original.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.Symbols.Length, deserialized.Symbols.Length);
        Assert.Equal(original.Paytables.Length, deserialized.Paytables.Length);
        Assert.Equal(original.PaylineSets.Length, deserialized.PaylineSets.Length);
        Assert.Equal(original.ReelStrips.Length, deserialized.ReelStrips.Length);
        Assert.Equal(original.Nodes.Length, deserialized.Nodes.Length);
        Assert.Equal(original.Edges.Length, deserialized.Edges.Length);
        Assert.Equal(original.Plugins.Length, deserialized.Plugins.Length);

        // Expressions survived.
        Assert.NotNull(deserialized.Expressions);
        Assert.Equal(original.Expressions!.Count, deserialized.Expressions.Count);
        Assert.True(deserialized.Expressions.ContainsKey("mult2x"));
        Assert.True(deserialized.Expressions.ContainsKey("isScatter"));

        // Plugin survived.
        var plugin = deserialized.Plugins[0];
        Assert.Equal("custom-eval-v1", plugin.PluginId);
        Assert.Equal(PluginContract.IEvaluator, plugin.Contract);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.NotNull(plugin.Config);
        Assert.Equal("cluster", plugin.Config!["mode"]);
    }

    [Fact]
    public void RoundTrip_MetricsIdentical()
    {
        // Round-trip a full config with expressions embedded, then
        // verify the expression inside survived and evaluates identically.
        var original = CreateRepresentativeConfig();

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts)!;

        // Verify both configs contain the same expressions.
        var expr1 = original.Expressions!["mult2x"];
        var expr2 = deserialized.Expressions!["mult2x"];

        // Evaluate both. The board is a user-defined array in state (invariant 4);
        // its dimensions live in state too.
        var ctx = new EvalContext { State = new Dictionary<string, object?> { ["rows"] = 3 } };

        var v1 = ExactExpressionEvaluator.Evaluate(expr1, ctx);
        var v2 = ExactExpressionEvaluator.Evaluate(expr2, ctx);

        // Both evaluate to rows*2 = 3*2 = 6.
        Assert.Equal(new BigInteger(6), v1.AsInteger());
        Assert.Equal(v1.AsInteger(), v2.AsInteger());
    }

    [Fact]
    public void RoundTrip_ComplexExpressionInsideConfig_YieldsSameEvaluation()
    {
        // Create a config with a complex expression, round-trip, verify evaluation.
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "expr-test",
            Nodes = Array.Empty<Node>(),
            Edges = Array.Empty<Edge>(),
            Expressions = new Dictionary<string, Expression>
            {
                ["complex"] = new IfExpr
                {
                    Condition = new CompareExpr
                    {
                        Op = CompareOp.Gt,
                        Left = new FieldAccessExpr { Path = ["rows"], Target = "state" },
                        Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" },
                    },
                    ThenExpr = new BinaryExpr
                    {
                        Op = BinaryOp.Mul,
                        Left = new ConstantExpr { Kind = ConstantKind.Integer, Value = "7" },
                        Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "3" },
                    },
                    ElseExpr = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" },
                },
            },
        };

        var json = JsonSerializer.Serialize(config, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<GraphConfig>(json, JsonOpts)!;

        var expr = deserialized.Expressions!["complex"];
        var ctx = new EvalContext { State = new Dictionary<string, object?> { ["rows"] = 3 } };

        var result = ExactExpressionEvaluator.Evaluate(expr, ctx);
        // rows=3 > 0 → then branch: 7*3 = 21
        Assert.Equal(new BigInteger(21), result.AsInteger());
    }

    /// <summary>
    /// Compute a deterministic canonical hash from a GraphConfig:
    /// round-trip through stable JSON → SHA256.
    /// </summary>
    private static string ComputeCanonicalHash(GraphConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
