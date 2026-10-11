using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

[Collection("SamplingOptions")]
public sealed class TumbleCompositionTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(5, true)]
    [InlineData(9, true)]
    public void EveryColumnPatternPreservesSurvivorOrderFreshDrawOrderAndCarriedValues(int rows, bool reels)
    {
        var file = $"authoring.tumble.{rows}.{(reels ? "reels" : "cells")}.json";
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Games", file)), JsonOptions.Default)!;
        CustomMechanic fall = config.Mechanics!["tumble"];
        ModifyStateNode[] writes = fall.Nodes.OfType<ModifyStateNode>()
            .Where(node =>
                node.OutputKey is "emptyBefore" or "columnRemoved" or "survivors" or "heldSurvivors" or "board"
                    or "held")
            .ToArray();
        Dictionary<string, object?> initial = config.InitialState!.ToDictionary(pair => pair.Key,
            pair => InitialStateValues.Materialize(pair.Value));
        Dictionary<string, int> layout = initial.Keys.Concat(writes.Select(node => node.OutputKey!))
            .Concat(["item", "place", "stop0", "stop1", "stop2", "topDrop0", "topDrop1", "topDrop2"])
            .Distinct().Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);
        foreach (SamplingTier tier in new[] { SamplingTier.Interpreted, SamplingTier.Tiered, SamplingTier.Compiled })
        {
            var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial);
            var compiler = new SamplingExpressions(key => layout[key], tier);
            Func<SamplingFrame, ExprValue>[] selectors =
                writes.Select(node => compiler.Compile(fall.Expressions![node.ExpressionId!])).ToArray();
            for (var pattern = 0; pattern < 1 << rows; pattern++)
            {
                // Each pattern occurs in each column; its neighbors use different patterns, including full and empty.
                var patterns = new[] { pattern, ((1 << rows) - 1) ^ pattern, pattern / 2 };
                var removed = patterns.Select((mask, column) => (ulong)mask << (column * rows))
                    .Aggregate(0UL, (left, right) => left | right);
                var state = new Dictionary<string, object?>(initial);
                state["removed"] = new BigInteger(removed);
                var count = BitOperations.PopCount(removed);
                state["drops"] = Enumerable.Range(0, count).Select(index => (1000 + index).ToString()).ToArray();
                var expectedBoard = new List<string>();
                var expectedHeld = new List<BigInteger>();
                var drawn = 0;
                for (var column = 0; column < 3; column++)
                {
                    var keep = Enumerable.Range(0, rows).Where(row => (patterns[column] & (1 << row)) == 0).ToArray();
                    var empty = rows - keep.Length;
                    state[$"stop{column}"] = "0";
                    state[$"topDrop{column}"] = empty - 1;
                    for (var row = 0; row < empty; row++)
                    {
                        expectedBoard.Add((reels ? 100 + column * 20 + empty - 1 - row : 1000 + drawn + empty - 1 - row)
                            .ToString());
                        expectedHeld.Add(-1);
                    }

                    foreach (var row in keep)
                    {
                        expectedBoard.Add((column * rows + row).ToString());
                        expectedHeld.Add(column * rows + row + 10);
                    }

                    drawn += empty;
                }

                frame.Reset(default);
                foreach (var (key, value) in state)
                {
                    frame.Cells[layout[key]] = SamplingCell.FromRaw(value);
                }

                for (var step = 0; step < writes.Length; step++)
                {
                    ModifyStateNode node = writes[step];
                    ExprValue exact = ExactExpressionEvaluator.Evaluate(fall.Expressions![node.ExpressionId!],
                        new EvalContext
                        {
                            State = state
                        });
                    ExprValue sampled = selectors[step](frame);
                    Assert.Equal(exact, sampled);
                    state[node.OutputKey!] = exact.ToStateObject();
                    frame.Cells[layout[node.OutputKey!]].SetTyped(sampled);
                }

                Assert.Equal(expectedBoard,
                    frame.Read(layout["board"], "board").ArrayValue!.Select(value => value.StringValue));
                Assert.Equal(expectedHeld,
                    frame.Read(layout["held"], "held").ArrayValue!.Select(value => value.AsInteger()));
            }
        }
    }
}
