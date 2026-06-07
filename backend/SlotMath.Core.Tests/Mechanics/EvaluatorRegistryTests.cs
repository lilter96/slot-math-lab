using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests.Mechanics;

[Collection("Registry")]
public class EvaluatorRegistryTests : IDisposable
{
    public EvaluatorRegistryTests() => EvaluatorRegistry.Clear();
    public void Dispose() => EvaluatorRegistry.Clear();

    // ── Win model ───────────────────────────────────────────────────

    [Fact]
    public void Win_RecordsSymbolIdCountAndPositions()
    {
        var positions = new[] { (0, 0), (0, 1), (0, 2) };
        var win = new Win
        {
            SymbolId = "A",
            Count = 3,
            Positions = positions,
            Payout = 10m
        };

        Assert.Equal("A", win.SymbolId);
        Assert.Equal(3, win.Count);
        Assert.Equal(3, win.Positions.Length);
        Assert.Equal(10m, win.Payout);
    }

    [Fact]
    public void Win_CanCarryEvaluatorName()
    {
        var win = new Win
        {
            SymbolId = "WILD",
            Count = 5,
            Positions = new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },
            Payout = 100m,
            EvaluatorName = "Lines"
        };

        Assert.Equal("Lines", win.EvaluatorName);
    }

    [Fact]
    public void Win_CanHaveMultiplier()
    {
        var win = new Win
        {
            SymbolId = "A",
            Count = 3,
            Positions = new[] { (0, 0), (0, 1), (0, 2) },
            Payout = 10m,
            Multiplier = 2m
        };

        Assert.Equal(20m, win.TotalWin);
    }

    [Fact]
    public void Win_StructuralEquality()
    {
        var a = new Win { SymbolId = "A", Count = 3, Positions = new[] { (0, 0), (0, 1) }, Payout = 5m };
        var b = new Win { SymbolId = "A", Count = 3, Positions = new[] { (0, 0), (0, 1) }, Payout = 5m };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ── IEvaluator ──────────────────────────────────────────────────

    [Fact]
    public void IEvaluator_IsPureFunction()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } });

        var evaluator = new PureTestEvaluator();

        var wins1 = evaluator.Evaluate(board, null);
        var wins2 = evaluator.Evaluate(board, null);

        // Same input → same output
        Assert.Equal(wins1.Length, wins2.Length);
        for (var i = 0; i < wins1.Length; i++)
            Assert.Equal(wins1[i], wins2[i]);
    }

    [Fact]
    public void IEvaluator_NoDrawNoIO()
    {
        var evaluator = new PureTestEvaluator();
        var board = new Board(2, 2);

        // Apply multiple times with same input — always deterministic
        var r1 = evaluator.Evaluate(board, null);
        var r2 = evaluator.Evaluate(board, null);
        var r3 = evaluator.Evaluate(board, null);

        Assert.Equal(r1.Length, r2.Length);
        Assert.Equal(r2.Length, r3.Length);
    }

    // ── Registry ────────────────────────────────────────────────────

    [Fact]
    public void Registry_StartsEmpty()
    {
        Assert.Empty(EvaluatorRegistry.All);
    }

    [Fact]
    public void Registry_Register_AddsEvaluator()
    {
        var eval = new PureTestEvaluator();
        EvaluatorRegistry.Register("test-eval", eval);
        Assert.Same(eval, EvaluatorRegistry.TryGet("test-eval"));
    }

    [Fact]
    public void Registry_DuplicateName_Throws()
    {
        EvaluatorRegistry.Register("dup", new PureTestEvaluator());
        Assert.Throws<InvalidOperationException>(() =>
            EvaluatorRegistry.Register("dup", new PureTestEvaluator()));
    }

    [Fact]
    public void Registry_Clear_RemovesAll()
    {
        EvaluatorRegistry.Register("x", new PureTestEvaluator());
        EvaluatorRegistry.Clear();
        Assert.Empty(EvaluatorRegistry.All);
    }
}

// ── Test evaluator ─────────────────────────────────────────────────────

public sealed class PureTestEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);
        return Array.Empty<Win>();
    }
}
