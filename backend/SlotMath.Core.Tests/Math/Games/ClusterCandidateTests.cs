using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

public sealed class ClusterCandidateTests
{
    private const int Rows = 5;
    private const int Columns = 6;
    private const int Cells = Rows * Columns;

    private static readonly CustomMechanic Candidates = JsonSerializer.Deserialize<GraphConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Games", "nine-lives.json")),
        JsonOptions.Default)!.Mechanics!["cluster-candidates"];

    [Fact]
    public void EveryConnectedComponentBelowFiveIsExcluded_AndEveryFiveCellComponentRetainsASeed()
    {
        HashSet<ulong> components = Enumerable.Range(0, Cells).Select(cell => 1UL << cell).ToHashSet();
        for (var size = 1; size <= 5; size++)
        {
            if (size == 4)
            {
                Assert.Equal(295, components.Count);
            }

            if (size == 5)
            {
                Assert.Equal(766, components.Count);
            }

            foreach (var component in components)
            {
                var board = Enumerable.Range(0, Cells).Select(cell => (component & (1UL << cell)) == 0 ? 7 : 1)
                    .ToArray();
                var seeds = Select(board);
                Assert.Equal(0UL, seeds & ~component);
                Assert.True(size < 5 ? seeds == 0 : seeds != 0, $"size={size}, cells={component:x}, seeds={seeds:x}");
            }

            if (size == 5)
            {
                break;
            }

            var next = new HashSet<ulong>();
            foreach (var component in components)
            {
                foreach (var cell in Enumerable.Range(0, Cells).Where(cell => (component & (1UL << cell)) != 0))
                {
                    foreach (var neighbor in Neighbors(cell))
                    {
                        if ((component & (1UL << neighbor)) == 0)
                        {
                            next.Add(component | (1UL << neighbor));
                        }
                    }
                }
            }

            components = next;
        }
    }

    [Fact]
    public void MixedSymbolsAndLargerComponentsAgreeWithIndependentFloodFill()
    {
        var random = new System.Random(7819);
        for (var trial = 0; trial < 2_000; trial++)
        {
            var board = Enumerable.Range(0, Cells).Select(_ => random.Next(1, 5)).ToArray();
            var seeds = Select(board);
            var visited = new HashSet<int>();
            foreach (var first in Enumerable.Range(0, Cells))
            {
                if (!visited.Add(first))
                {
                    continue;
                }

                var component = new HashSet<int>
                {
                    first
                };
                var queue = new Queue<int>();
                queue.Enqueue(first);
                while (queue.TryDequeue(out var cell))
                {
                    foreach (var neighbor in Neighbors(cell))
                    {
                        if (board[neighbor] == board[first] && visited.Add(neighbor))
                        {
                            component.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                var mask = component.Aggregate(0UL, (value, cell) => value | (1UL << cell));
                Assert.True(component.Count < 5 ? (seeds & mask) == 0 : (seeds & mask) != 0,
                    $"trial={trial}, size={component.Count}, cells={mask:x}, seeds={seeds:x}");
            }
        }
    }

    // Geometry uses explicit row/column coordinates, independently of the authored shift/mask expressions.
    private static IEnumerable<int> Neighbors(int cell)
    {
        var column = cell / Rows;
        var row = cell % Rows;
        if (row > 0)
        {
            yield return cell - 1;
        }

        if (row + 1 < Rows)
        {
            yield return cell + 1;
        }

        if (column > 0)
        {
            yield return cell - Rows;
        }

        if (column + 1 < Columns)
        {
            yield return cell + Rows;
        }
    }

    private static ulong Select(int[] board)
    {
        var state = new Dictionary<string, object?>
        {
            ["unpaidCells"] = board.Select((symbol, cell) => symbol == 7 ? 0UL : 1UL << cell)
                .Aggregate(0UL, (a, b) => a | b)
        };
        for (var digit = 0; digit < 3; digit++)
        {
            state[$"symbolDigit{digit}"] = board
                .Select((symbol, cell) => (symbol & (1 << digit)) == 0 ? 0UL : 1UL << cell)
                .Aggregate(0UL, (a, b) => a | b);
        }

        foreach (ModifyStateNode? node in Candidates.Nodes.Cast<ModifyStateNode>())
        {
            state[node.OutputKey!] = ExactExpressionEvaluator.EvaluateAsInteger(
                Candidates.Expressions![node.ExpressionId!],
                new EvalContext
                {
                    State = state
                });
        }

        return (ulong)(BigInteger)state["candidateCells"]!;
    }
}
