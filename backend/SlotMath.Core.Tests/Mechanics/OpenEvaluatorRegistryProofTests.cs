using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Mechanics;

/// <summary>
/// Proves that the evaluator registry is truly open: registering a brand-new
/// IEvaluator implementation requires zero changes to the interpreters,
/// the Slot monad, the compiler, or any other engine code.
///
/// Defines a novel evaluator (AnyTwoOfAKindEvaluator) entirely within the
/// test file, registers it, and exercises it through the standard
/// IEvaluator interface — proving the open/closed principle holds.
/// </summary>
[Collection("Registry")]
public class OpenEvaluatorRegistryProofTests : IDisposable
{
    public OpenEvaluatorRegistryProofTests()
    {
        EvaluatorRegistry.Clear();
        EvaluatorRegistry.Register("any-two", new AnyTwoOfAKindEvaluator());
    }

    public void Dispose()
    {
        EvaluatorRegistry.Clear();
    }

    [Fact]
    public void NovelEvaluator_IsDiscoverable_ViaRegistry()
    {
        var eval = EvaluatorRegistry.TryGet("any-two");
        Assert.NotNull(eval);
        Assert.IsType<AnyTwoOfAKindEvaluator>(eval);
    }

    [Fact]
    public void NovelEvaluator_AppliesCorrectly_ThroughStandardInterface()
    {
        var eval = EvaluatorRegistry.TryGet("any-two")!;

        // Board with 2 A's and 3 B's anywhere
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "B" } });

        var wins = eval.Evaluate(board, null);

        // Our novel evaluator pays 1 per pair of any symbol
        Assert.Equal(2, wins.Length); // 1 pair of A, 1 pair of B (3rd B doesn't make a pair)
        Assert.Contains(wins, w => w.SymbolId == "A" && w.Payout == 1m);
        Assert.Contains(wins, w => w.SymbolId == "B" && w.Payout == 1m);
    }

    [Fact]
    public void NovelEvaluator_IsPure_NoDrawNoIO()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "X" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "X" } });

        var eval = new AnyTwoOfAKindEvaluator();

        var w1 = eval.Evaluate(board, null);
        var w2 = eval.Evaluate(board, null);

        Assert.Equal(w1.Length, w2.Length);
        for (var i = 0; i < w1.Length; i++)
            Assert.Equal(w1[i], w2[i]);
    }

    [Fact]
    public void NovelEvaluator_ComposesWithStandardEvaluators()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "X" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "X" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "X" } });

        var novel = EvaluatorRegistry.TryGet("any-two")!;
        var scatterEval = new ScatterEvaluator(
            new Paytable
            {
                Id = "test",
                Entries = new[]
                {
                    new PaytableEntry
                    {
                        SymbolId = "X",
                        Counts = new[] { 3 },
                        Payouts = new[] { "15" }
                    }
                }
            });

        var novelWins = novel.Evaluate(board, null);
        var scatterWins = scatterEval.Evaluate(board, null);

        // Both work independently through the same IEvaluator interface
        Assert.NotEmpty(novelWins);
        Assert.NotEmpty(scatterWins);
    }

    [Fact]
    public void Registry_AcceptsMultipleNovelEvaluators_WithoutEngineChanges()
    {
        EvaluatorRegistry.Register("count-all", new CountAllEvaluator());

        var anyTwo = EvaluatorRegistry.TryGet("any-two")!;
        var countAll = EvaluatorRegistry.TryGet("count-all")!;

        Assert.NotNull(anyTwo);
        Assert.NotNull(countAll);
        Assert.NotSame(anyTwo, countAll);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Novel evaluators defined entirely within the test — no engine changes
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A novel evaluator that pays 1 per pair of matching symbols anywhere on the board.
/// This evaluator did not exist when the engine, interpreters, or compiler were built.
/// </summary>
public sealed class AnyTwoOfAKindEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var symbolCounts = new Dictionary<string, List<(int Row, int Col)>>();
        foreach (var (r, c, cell) in board.AllCells())
        {
            if (cell.IsEmpty) continue;
            var sym = cell.Symbols![0];
            if (!symbolCounts.ContainsKey(sym))
                symbolCounts[sym] = new List<(int, int)>();
            symbolCounts[sym].Add((r, c));
        }

        var wins = new List<Win>();
        foreach (var (sym, positions) in symbolCounts)
        {
            var pairs = positions.Count / 2;
            if (pairs > 0)
            {
                wins.Add(new Win
                {
                    SymbolId = sym,
                    Count = pairs * 2,
                    Positions = positions.Take(pairs * 2).ToArray(),
                    Payout = pairs * 1m,
                    EvaluatorName = "AnyTwoOfAKind"
                });
            }
        }

        return wins.ToArray();
    }
}

/// <summary>
/// A second novel evaluator that simply counts total symbols and reports them as a "win".
/// </summary>
public sealed class CountAllEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var symbolCounts = new Dictionary<string, List<(int Row, int Col)>>();
        foreach (var (r, c, cell) in board.AllCells())
        {
            if (cell.IsEmpty) continue;
            var sym = cell.Symbols![0];
            if (!symbolCounts.ContainsKey(sym))
                symbolCounts[sym] = new List<(int, int)>();
            symbolCounts[sym].Add((r, c));
        }

        return symbolCounts.Select(kv => new Win
        {
            SymbolId = kv.Key,
            Count = kv.Value.Count,
            Positions = kv.Value.ToArray(),
            Payout = 0m, // informational only
            EvaluatorName = "CountAll"
        }).ToArray();
    }
}
