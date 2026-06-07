using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  Test evaluators used by the G12 plugin test suite.
//
//  These live in the test project (not in Core) to simulate the plugin
//  scenario: a novel evaluator that did not exist when the engine was built.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A conformant plugin evaluator — pure, deterministic, fast.
/// Pays 1 per pair of matching symbols on the board.
/// </summary>
public sealed class ConformantTestEvaluator : IEvaluator
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
                    EvaluatorName = "ConformantTest",
                });
            }
        }

        return wins.ToArray();
    }
}

/// <summary>
/// A misbehaving plugin that throws an exception during evaluation.
/// Used to verify sandbox containment.
/// </summary>
public sealed class ThrowingTestEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        throw new InvalidOperationException("Plugin intentionally threw an error.");
    }
}

/// <summary>
/// A plugin that attempts I/O during evaluation (Console.WriteLine).
/// The production sandbox (AssemblyLoadContext) blocks this; in tests
/// we verify the harness still validates the evaluator.
/// </summary>
public sealed class IOAttemptingTestEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        // Attempt I/O — would be blocked by production sandbox.
        Console.WriteLine("Plugin attempted I/O — this is a test.");

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
            if (positions.Count >= 2)
            {
                wins.Add(new Win
                {
                    SymbolId = sym,
                    Count = positions.Count,
                    Positions = positions.ToArray(),
                    Payout = positions.Count * 1m,
                    EvaluatorName = "IOAttemptingTest",
                });
            }
        }

        return wins.ToArray();
    }
}

/// <summary>
/// A misbehaving plugin that loops forever.
/// Used to verify sandbox timeout termination.
/// </summary>
public sealed class InfiniteLoopTestEvaluator : IEvaluator
{
    public Win[] Evaluate(Board board, object? state)
    {
        // Busy-loop — should be terminated by sandbox timeout.
        while (true)
        {
            // Keep the CPU busy.
            Thread.SpinWait(1000);
        }
    }
}
