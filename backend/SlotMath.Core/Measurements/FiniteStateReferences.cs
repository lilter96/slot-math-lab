using SlotMath.Core.Math;
namespace SlotMath.Core.Measurements;

public static partial class FiniteModelAnalysis
{
    private static Rational[] Solve(Rational[,] coefficients, Rational[] rhs, CancellationToken token)
    {
        var n = rhs.Length; var a = new Rational[n, n + 1];
        for (var i = 0; i < n; i++) { for (var j = 0; j < n; j++) a[i, j] = coefficients[i, j]; a[i, n] = rhs[i]; }
        for (var c = 0; c < n; c++)
        {
            token.ThrowIfCancellationRequested(); var pivot = c;
            while (pivot < n && a[pivot, c] == Rational.Zero) pivot++;
            if (pivot == n) throw new ArithmeticException("Finite-state reference linear system is singular.");
            if (pivot != c) for (var j = c; j <= n; j++) (a[pivot, j], a[c, j]) = (a[c, j], a[pivot, j]);
            var divisor = a[c, c]; for (var j = c; j <= n; j++) a[c, j] = Budget(a[c, j] / divisor);
            for (var i = 0; i < n; i++) if (i != c) { var factor = a[i, c]; for (var j = c; j <= n; j++) a[i, j] = Budget(a[i, j] - factor * a[c, j]); }
        }
        return Enumerable.Range(0, n).Select(i => a[i, n]).ToArray();
    }
    private static Rational Absorption(Rational[][] q, bool[] canExit, int initial, CancellationToken token)
    {
        if (!canExit[initial]) return Rational.Zero;
        var states = Enumerable.Range(0, q.Length).Where(i => canExit[i]).ToArray(); var k = states.Length;
        var matrix = new Rational[k, k]; var exit = new Rational[k];
        for (var i = 0; i < k; i++)
        {
            exit[i] = Rational.One - q[states[i]].Aggregate(Rational.Zero, (a, b) => Budget(a + b));
            for (var j = 0; j < k; j++) matrix[i, j] = (i == j ? Rational.One : Rational.Zero) - q[states[i]][states[j]];
        }
        return Solve(matrix, exit, token)[Array.IndexOf(states, initial)];
    }
    private static MarkovModelReport Stationary(Rational[][] p, Rational[] rewards, Rational[] costs, int initial, CancellationToken token)
    {
        var n = p.Length;
        if (p.Any(row => row.Aggregate(Rational.Zero, (a, b) => Budget(a + b)) != Rational.One)) throw new ArgumentException("Stationary models require every transition row to sum exactly to one.");
        var reach = new bool[n, n];
        for (var i = 0; i < n; i++) { reach[i, i] = true; for (var j = 0; j < n; j++) if (p[i][j] > Rational.Zero) reach[i, j] = true; }
        for (var k = 0; k < n; k++) { token.ThrowIfCancellationRequested(); for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) reach[i, j] |= reach[i, k] && reach[k, j]; }
        var reachable = Enumerable.Range(0, n).Where(i => reach[initial, i]).ToArray(); var remaining = new HashSet<int>(reachable); var closed = new List<int[]>();
        while (remaining.Count > 0)
        {
            var start = remaining.Min(); var component = remaining.Where(j => reach[start, j] && reach[j, start]).Order().ToArray();
            remaining.ExceptWith(component); var set = component.ToHashSet();
            if (component.All(i => Enumerable.Range(0, n).All(j => p[i][j] == Rational.Zero || set.Contains(j)))) closed.Add(component);
        }
        if (closed.Count != 1) return new("nonuniqueStationary", [], null, null, reachable, "Multiple recurrent classes are reachable. A unique stationary occupancy and unconditional long-run return are withheld; supply a class or an independently justified absorption mixture.");
        var states = closed[0]; var size = states.Length; var matrix = new Rational[size, size]; var rhs = Enumerable.Repeat(Rational.Zero, size).ToArray(); rhs[^1] = Rational.One;
        for (var i = 0; i < size; i++) for (var j = 0; j < size; j++) matrix[i, j] = i == size - 1 ? Rational.One : p[states[j]][states[i]] - (i == j ? Rational.One : Rational.Zero);
        var pi = Solve(matrix, rhs, token); var occupancy = Enumerable.Repeat(Rational.Zero, n).ToArray(); Rational reward = Rational.Zero, cost = Rational.Zero;
        for (var i = 0; i < size; i++) { occupancy[states[i]] = pi[i]; reward = Budget(reward + pi[i] * rewards[states[i]]); cost = Budget(cost + pi[i] * costs[states[i]]); }
        return new("stationary", [], null, null, reachable, "Exact stationary occupation for the unique reachable recurrent class. Long-run return is reward / cost under that occupancy. Periodic chains may oscillate at a fixed time; the result describes long-run occupation averages. This supplied state model is separate from the compiled graph.")
        { StationaryOccupancy = occupancy.Select(v => v.ToString()).ToArray(), LongRunRewardPerStep = reward.ToString(), LongRunCostPerStep = cost.ToString(), LongRunReturn = (reward / cost).ToString() };
    }
}
