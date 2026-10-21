using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Math.Structured;

internal sealed partial class LoopChain
{
    public LoopExits? RunMatrix(ExprValue entry) => _template.Cap >= 64 ? TryMatrix(entry) : null;

    /// <summary>
    ///     Discover a small reachable state space from the same symbolic kernel as the transfer solver,
    ///     then compose finite-horizon transition operators. A stop check is a zero-reward transition to
    ///     a separate absorbing exit: a state reached on the final body iteration still exits at the cap.
    ///     Failed optimization attempts leave the original transfer solver available.
    /// </summary>
    private LoopExits? TryMatrix(ExprValue entry)
    {
        var originalLimit = _f.Budget.Limit;
        var started = _f.Budget.Used;
        var allowance = System.Math.Min(_matrixWork, (originalLimit - started) / 4);
        if (_matrixStates <= 0 || _accumulators > 64 || allowance < 4096)
        {
            return null;
        }

        // Finish also widens the shared metadata before charging its work. A rejected attempt must
        // not leave those tentative bounds behind; cached kernel moments remain valid for fallback.
        var priorBounded = (bool[])Bounded.Clone();
        var priorSigned = (bool[])Signed.Clone();
        var priorStep = (Rational[])Step.Clone();
        var priorLo = (ExprValue[])Lo.Clone();
        var priorHi = (ExprValue[])Hi.Clone();
        var succeeded = false;
        _f.Budget.Limit = started + allowance;
        try
        {
            _f.Budget.Check();
            var states = new List<MatrixState>();
            var positions = new Dictionary<ExprValue, int>(V.ValueComparer.Instance);
            var pending = new Queue<int>();

            int Add(ExprValue state, int depth)
            {
                if (positions.TryGetValue(state, out var known))
                {
                    return known;
                }

                if (states.Count >= System.Math.Min(_matrixStates, 128))
                {
                    throw new MarkovMatrixLimitException("reachable loop state space exceeds the matrix budget");
                }

                var index = states.Count;
                positions.Add(state, index);
                states.Add(new MatrixState(state, depth));
                pending.Enqueue(index);
                return index;
            }

            Add(entry, 0);
            while (pending.TryDequeue(out var index))
            {
                MatrixState state = states[index];
                // Its outgoing transitions cannot be used within this horizon. In particular, do not
                // evaluate a failing expression that only becomes reachable after the authored cap.
                if (state.Depth >= _template.Cap)
                {
                    continue;
                }

                Kernel kernel = KernelOf(state.Value);
                state.Kernel = kernel;
                if (kernel.Stopped)
                {
                    continue;
                }

                for (var z = 0; z < Classes; z++)
                {
                    if (!kernel.Probability[z].IsZero)
                    {
                        Add(kernel.Targets[z], state.Depth + 1);
                    }
                }
            }

            var transitions = new List<MarkovRewardTransition>();
            var physicalStates = states.Count;
            for (var i = 0; i < physicalStates; i++)
            {
                MatrixState state = states[i];
                if (state.Kernel is not { } kernel)
                {
                    transitions.Add(Identity(i, i));
                    continue;
                }

                if (kernel.Stopped)
                {
                    if (states.Count >= System.Math.Min(_matrixStates, 128))
                    {
                        throw new MarkovMatrixLimitException("absorbing loop exits exceed the matrix state budget");
                    }

                    var exit = states.Count;
                    states.Add(new MatrixState(state.Value, state.Depth)
                    {
                        ExitReason = kernel.Reason
                    });
                    transitions.Add(Identity(i, exit));
                    transitions.Add(Identity(exit, exit));
                    continue;
                }

                for (var z = 0; z < Classes; z++)
                {
                    if (kernel.Probability[z].IsZero)
                    {
                        continue;
                    }

                    transitions.Add(new MarkovRewardTransition(i, positions[kernel.Targets[z]], kernel.Probability[z],
                        kernel.First.Select(row => row[z]).ToArray(), kernel.Second.Select(row => row[z]).ToArray(),
                        kernel.Zero.Select(row => row is null ? (Rational?)null : row[z]).ToArray(),
                        kernel.Bounds.Select(bound => bound is { } b ? Number(b.Lo) : (Rational?)null).ToArray(),
                        kernel.Bounds.Select(bound => bound is { } b ? Number(b.Hi) : (Rational?)null).ToArray(),
                        kernel.Higher.Select(row => row[z]).ToArray()));
                }
            }

            void Spend(long units)
            {
                _f.Budget.Tick(units);
                _f.Budget.Check();
            }

            var matrix = MarkovRewardMatrix.Create(states.Count, _accumulators, transitions, Spend, higher: _higher);
            Rational pruned = Rational.Zero;

            bool Stop(int steps, MarkovRewardMatrix row)
            {
                BigInteger alive = BigInteger.Zero;
                foreach ((var target, MarkovRewardMatrix.Weight weight) in row.Row(0))
                {
                    if (target < physicalStates && states[target].Kernel?.Stopped != true)
                    {
                        alive += weight.Mass;
                    }
                }

                if (alive.IsZero)
                {
                    return true;
                }

                if (_template.PruneBelow is { } epsilon &&
                    (row.Denominator.GetBitLength() > _template.PruneAboveBits ||
                     _f.Budget.Used - started > _template.PruneAboveWork) &&
                    alive * epsilon.Denominator < epsilon.Numerator * row.Denominator)
                {
                    pruned = new Rational(alive, row.Denominator);
                    return true;
                }

                return false;
            }

            (MarkovRewardMatrix result, var steps) = matrix.Advance(0, _template.Cap, Stop);
            var done = new Dictionary<ExprValue, Cell>(V.ValueComparer.Instance);
            foreach ((var target, MarkovRewardMatrix.Weight weight) in result.Row(0))
            {
                MatrixState state = states[target];
                var reason = state.ExitReason ??
                             (steps < _template.Cap && state.Kernel is { Stopped: true } stopped
                                 ? stopped.Reason
                                 : "modelLimit");
                var cell = new Cell(_accumulators, _pairs, HigherCount)
                {
                    Mass = weight.Mass,
                    Ranged = true
                };
                for (var a = 0; a < _accumulators; a++)
                {
                    cell.First[a] = weight.First[a];
                    cell.Zero[a] = weight.Zero[a] ?? BigInteger.Zero;
                    Signed[a] &= weight.Zero[a].HasValue;
                    if (weight.Lower[a] is { } lo && weight.Upper[a] is { } hi)
                    {
                        cell.Lo[a] = Value(lo);
                        cell.Hi[a] = Value(hi);
                    }
                    else
                    {
                        Bounded[a] = false;
                        cell.Lo[a] = cell.Hi[a] = V.Zero;
                    }
                }

                Array.Copy(weight.Second, cell.Second, _pairs);
                Array.Copy(weight.Higher, cell.Higher, HigherCount);
                Absorb(done, Exit(state.Value, reason), cell);
            }

            foreach (MatrixState state in states.Take(physicalStates))
            {
                if (state.Kernel is not { Stopped: false } kernel)
                {
                    continue;
                }

                for (var a = 0; a < _accumulators; a++)
                {
                    if (kernel.Bounds[a] is { } bound)
                    {
                        Step[a] = Inference.Common(Step[a], bound.Step);
                    }
                }
            }

            LoopExits exits = Finish(done, result.Denominator);
            exits.Pruned = pruned;
            MatrixRuns++;
            succeeded = true;
            return exits;
        }
        catch (MarkovMatrixLimitException)
        {
            return null;
        }
        catch (StructuredBudgetException exhausted) when (exhausted.Work && _f.Budget.Used <= originalLimit)
        {
            return null;
        }
        catch (Exception exception) when (exception is LoopChainDeclinedException or StructuredDeclinedException)
        {
            return null;
        }
        finally
        {
            _f.Budget.Limit = originalLimit;
            if (!succeeded)
            {
                priorBounded.CopyTo(Bounded, 0);
                priorSigned.CopyTo(Signed, 0);
                priorStep.CopyTo(Step, 0);
                priorLo.CopyTo(Lo, 0);
                priorHi.CopyTo(Hi, 0);
            }
        }
    }

    private MarkovRewardTransition Identity(int from, int to) => new(from, to, Rational.One,
        Enumerable.Repeat(Rational.Zero, _accumulators).ToArray(),
        Enumerable.Repeat(Rational.Zero, _pairs).ToArray(),
        Enumerable.Repeat<Rational?>(Rational.One, _accumulators).ToArray(),
        Enumerable.Repeat<Rational?>(Rational.Zero, _accumulators).ToArray(),
        Enumerable.Repeat<Rational?>(Rational.Zero, _accumulators).ToArray(),
        Enumerable.Repeat(Rational.Zero, HigherCount).ToArray());

    private static Rational Number(ExprValue value) => new(value.NumberNumerator, value.NumberDenominator);
    private static ExprValue Value(Rational value) => ExprValue.Rational(value.Numerator, value.Denominator);

    private sealed class MatrixState(ExprValue value, int depth)
    {
        public string? ExitReason;
        public Kernel? Kernel;
        public ExprValue Value { get; } = value;
        public int Depth { get; } = depth;
    }
}
