using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Tests.Expressions;

public sealed class ReachableOracleTests
{
    [Fact]
    public void PinnedGraphsThrough64VerticesMatchAnIndependentQueueBfs()
    {
        var random = new System.Random(104729);
        foreach (var count in new[] { 1, 4, 15, 25, 63, 64 })
        {
            for (var sample = 0; sample < 64; sample++)
            {
                List<int>[] adjacency = Enumerable.Range(0, count)
                    .Select(_ => Enumerable.Range(0, count).Where(_ => random.Next(8) == 0).ToList()).ToArray();
                HashSet<int> allowed = Enumerable.Range(0, count).Where(_ => random.Next(4) != 0).ToHashSet();
                HashSet<int> seeds = Enumerable.Range(0, count).Where(_ => random.Next(8) == 0).ToHashSet();
                var expected = new HashSet<int>(seeds.Where(allowed.Contains));
                var pending = new Queue<int>(expected.Order());
                while (pending.TryDequeue(out var vertex))
                {
                    foreach (var neighbor in adjacency[vertex])
                    {
                        if (allowed.Contains(neighbor) && expected.Add(neighbor))
                        {
                            pending.Enqueue(neighbor);
                        }
                    }
                }

                // The oracle uses explicit collections. Encode only at the API boundary.
                BigInteger Encode(IEnumerable<int> vertices)
                {
                    return vertices.Aggregate(BigInteger.Zero,
                        (mask, vertex) => mask + BigInteger.Pow(2, vertex));
                }

                ExprValue rows = ExprValue.Array(adjacency.Select(row => ExprValue.Number(Encode(row))).ToArray());
                ExprValue result = ReachabilityData.Evaluate(rows, ExprValue.Number(Encode(allowed)),
                    ExprValue.Number(Encode(seeds)), count);
                Assert.Equal(Encode(expected), result.AsInteger());
            }
        }
    }
}
