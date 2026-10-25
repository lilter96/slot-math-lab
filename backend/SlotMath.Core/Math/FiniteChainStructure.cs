namespace SlotMath.Core.Math;

public sealed class FiniteCommunicatingClass
{
    internal FiniteCommunicatingClass(int[] states, bool closed, int period)
    {
        States = Array.AsReadOnly(states);
        IsClosed = closed;
        Period = period;
    }

    public IReadOnlyList<int> States { get; }
    public bool IsClosed { get; }

    /// <summary>GCD of return lengths; zero for an acyclic singleton.</summary>
    public int Period { get; }
}

/// <summary>Reachable positive-edge SCCs of a supplied substochastic kernel. Missing row mass kills the chain.</summary>
public sealed class FiniteChainStructure
{
    private FiniteChainStructure(int[] components, FiniteCommunicatingClass[] classes, long work)
    {
        ComponentOf = Array.AsReadOnly(components);
        Classes = Array.AsReadOnly(classes);
        AbsorptionCertain = classes.All(component => !component.IsClosed);
        Work = work;
    }

    /// <summary>Unreachable states have component -1.</summary>
    public IReadOnlyList<int> ComponentOf { get; }

    public IReadOnlyList<FiniteCommunicatingClass> Classes { get; }
    public bool AbsorptionCertain { get; }
    public long Work { get; }

    public static FiniteChainStructure Analyze(ExactSparseMatrix kernel, IReadOnlyList<int> starts,
        ExactLinearOptions? options = null, CancellationToken cancellation = default)
    {
        var budget = new ExactLinearBudget(options, cancellation);
        ExactChainArithmetic.Validate(kernel, false, ref budget);
        ArgumentNullException.ThrowIfNull(starts);
        if (starts.Count < 1 || starts.Count > kernel.States)
        {
            throw new ArgumentException("Supply a nonempty bounded initial support.");
        }

        foreach (var start in starts)
        {
            budget.Spend();
            if (start < 0 || start >= kernel.States)
            {
                throw new ArgumentOutOfRangeException(nameof(starts));
            }
        }

        var n = kernel.States;
        budget.Spend(10L * n + kernel.NonzeroEntries);
        var visited = new bool[n];
        var stack = new int[n];
        var cursors = new int[n];
        var order = new int[n];
        var finished = 0;
        // Iterative DFS avoids consuming the native stack on long authored chains.
        foreach (var start in starts)
        {
            if (visited[start])
            {
                continue;
            }

            var count = 1;
            stack[0] = start;
            visited[start] = true;
            while (count > 0)
            {
                budget.Spend();
                var state = stack[count - 1];
                ExactSparseMatrix.Entry[] row = kernel.Rows[state];
                if (cursors[state] == row.Length)
                {
                    order[finished++] = state;
                    count--;
                }
                else
                {
                    var target = row[cursors[state]++].To;
                    if (visited[target])
                    {
                        continue;
                    }

                    visited[target] = true;
                    stack[count++] = target;
                }
            }
        }

        // Reverse adjacency in CSR form; no dense reachability matrix is materialized.
        var offsets = new int[n + 1];
        foreach (ExactSparseMatrix.Entry[] row in kernel.Rows)
        {
            foreach (ExactSparseMatrix.Entry entry in row)
            {
                budget.Spend();
                offsets[entry.To + 1]++;
            }
        }

        for (var state = 1; state <= n; state++)
        {
            offsets[state] += offsets[state - 1];
        }

        var next = (int[])offsets.Clone();
        var reverse = new int[kernel.NonzeroEntries];
        for (var state = 0; state < n; state++)
        {
            foreach (ExactSparseMatrix.Entry entry in kernel.Rows[state])
            {
                budget.Spend();
                reverse[next[entry.To]++] = state;
            }
        }

        var componentOf = new int[n];
        Array.Fill(componentOf, -1);
        var members = new List<int[]>();
        for (var index = finished - 1; index >= 0; index--)
        {
            var start = order[index];
            if (componentOf[start] >= 0)
            {
                continue;
            }

            var id = members.Count;
            var component = new List<int>();
            var count = 1;
            stack[0] = start;
            componentOf[start] = id;
            while (count > 0)
            {
                budget.Spend();
                var state = stack[--count];
                component.Add(state);
                for (var incoming = offsets[state]; incoming < offsets[state + 1]; incoming++)
                {
                    budget.Spend();
                    var predecessor = reverse[incoming];
                    if (!visited[predecessor] || componentOf[predecessor] >= 0)
                    {
                        continue;
                    }

                    componentOf[predecessor] = id;
                    stack[count++] = predecessor;
                }
            }

            members.Add(component.ToArray());
        }

        var depth = new int[n];
        Array.Fill(depth, -1);
        var classes = new FiniteCommunicatingClass[members.Count];
        for (var id = 0; id < members.Count; id++)
        {
            var start = members[id][0];
            var closed = true;
            var period = 0;
            var count = 1;
            var at = 0;
            stack[0] = start;
            depth[start] = 0;
            while (at < count)
            {
                budget.Spend();
                var state = stack[at++];
                closed &= kernel.RowSum(state) == Rational.One;
                foreach (ExactSparseMatrix.Entry entry in kernel.Rows[state])
                {
                    budget.Spend();
                    if (componentOf[entry.To] != id)
                    {
                        closed = false;
                        continue;
                    }

                    if (depth[entry.To] < 0)
                    {
                        depth[entry.To] = depth[state] + 1;
                        stack[count++] = entry.To;
                    }

                    period = Gcd(period, System.Math.Abs(depth[state] + 1 - depth[entry.To]));
                }
            }

            classes[id] = new FiniteCommunicatingClass(members[id], closed, period);
        }

        budget.Check();
        return new FiniteChainStructure(componentOf, classes, budget.Work);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
