using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  LoopChain — a loop as a Markov reward chain, solved instead of unrolled
//
//  Unrolling a loop to its cap gives a term as deep as the cap, and a
//  retrigger loop of 1,000 iterations is out of reach that way. Here the body
//  is executed once, symbolically, on a state of parameters. That one pass
//  gives the loop's transition kernel: for every value of the fields an
//  iteration reads (the chain state) the law of the next state together with
//  what the iteration adds to each accumulator.
//
//  An accumulator is a field the body only ever adds to (win := win + x) and
//  nothing else in the loop reads. It is held apart from the state: the chain
//  then has as many states as the game has feature states, not as many as it
//  has partial win totals. What the loop adds to it is carried by moments,
//
//      m0(s) = P(at s),  m1(s) = E[A; at s],  m2(s) = E[A·B; at s],
//
//  advanced one iteration at a time by the transfer recurrences
//
//      m0' = m0·p        m1' = m1·p + m0·r1
//      m2' = m2·p + m1ᵃ·r1ᵇ + m1ᵇ·r1ᵃ + m0·r2
//
//  with p, r1, r2 the kernel's probability and reward moments of a
//  transition. Every number is an integer over one running denominator, so a
//  step is big-integer multiply-and-add with no reduction. The cap is the
//  number of steps: the result is the exact value of the capped loop, not a
//  limit and not a bound.
//
//  The kernel's rewards come from the same structured inference as the rest
//  of the round, so one iteration may be a whole five-reel spin: the chain
//  needs its expectations given the transition, never its law.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>One symbolic pass over a loop body, split into what the chain needs.</summary>
internal sealed class LoopTemplate
{
    public required int Cap;

    /// <summary>What an iteration adds to each accumulator.</summary>
    public required Term[] Deltas;

    public required Term? ExitReason;

    /// <summary>Per state field: nothing reads it once the loop is over, so exits are not told apart by it.</summary>
    public required bool[] Forgotten;

    public required string Name;
    public required Term[] Next;
    public required List<Obligation> Obligations;

    /// <summary>The slots of every parameter of the pass.</summary>
    public required ulong[] ParamBits;

    public int PruneAboveBits;
    public long PruneAboveWork;

    /// <summary>
    ///     Once the numbers of a run are longer than <see cref="PruneAboveBits" />, or the run has taken more than
    ///     <see cref="PruneAboveWork" />, and the probability that the loop is still running is below this, the run
    ///     ends there as if at its cap. Null: never.
    /// </summary>
    public Rational? PruneBelow;

    /// <summary>The fields an iteration reads, with the parameters that stand for them and their next values.</summary>
    public required string[] StateKeys;

    public required ParamTerm[] StateParams;

    /// <summary>The stop condition and the exit reason as functions of the state; null for "never" and "condition".</summary>
    public required Term? Stop;
}

/// <summary>How a loop ends for one entry state: its exit classes, and each accumulator's moments within a class.</summary>
internal sealed class LoopExits
{
    /// <summary>Per class: the state fields at exit, then the exit reason.</summary>
    public required ExprValue[][] Classes;

    public required BigInteger[] Mass;

    /// <summary>Per class and accumulator: E[amount | class].</summary>
    public required Rational[][] Mean;

    /// <summary>The probability that the run was cut short of its cap; those executions are among the exits at the cap.</summary>
    public Rational Pruned = Rational.Zero;

    /// <summary>Per class and accumulator pair: E[amount·amount | class].</summary>
    public required Rational[][] Second;

    public required BigInteger Total;

    /// <summary>Per class and accumulator: P(amount = 0 | class), null when the amount can be negative.</summary>
    public required Rational?[][] Zero;
}

internal sealed class LoopChain
{
    private const int MaxTransitionClasses = 1 << 16;

    private static readonly string[] Reasons =
        ["condition", "modelLimit", "payoutCap", "authoredStop", "resourceExpiry"];

    public readonly bool[] Bounded, Signed;

    public readonly BigInteger[] Den;

    /// <summary>Proven range of what the loop adds to each accumulator, and a denominator the amounts divide into.</summary>
    public readonly ExprValue[] Lo, Hi;

    private readonly int _accumulators;
    private readonly TermFactory _f;
    private readonly Dictionary<int, Rational[]> _first = new();
    private readonly Inference _inference;
    private readonly Dictionary<ExprValue, Kernel> _kernels = new(V.ValueComparer.Instance);
    private readonly int _pairs;
    private readonly Dictionary<(int, int), Rational[]> _second = new();
    private readonly Dictionary<int, (Rational[]? Zero, Inference.Bound? Bound)> _signs = new();
    private readonly LoopTemplate _template;
    private readonly Term[] _transition;
    private readonly ulong[] _transitionBits;
    private readonly Term?[] _transitionIs;
    private readonly Law? _transitions;
    private BigInteger _denominator = BigInteger.One;
    private int _version;

    public LoopChain(LoopTemplate template, TermFactory factory, Inference inference)
    {
        _template = template;
        _f = factory;
        _inference = inference;
        _accumulators = template.Deltas.Length;
        _pairs = _accumulators * (_accumulators + 1) / 2;
        Lo = new ExprValue[_accumulators];
        Hi = new ExprValue[_accumulators];
        Den = new BigInteger[_accumulators];
        Bounded = new bool[_accumulators];
        Signed = new bool[_accumulators];
        Array.Fill(Lo, V.Zero);
        Array.Fill(Hi, V.Zero);
        Array.Fill(Den, BigInteger.One);
        Array.Fill(Bounded, true);
        Array.Fill(Signed, true);

        // Everything random about the next state: the largest sub-terms of the next-state terms that hold no
        // parameter. Their joint law is the same in every state, and so are the rewards given their value.
        var members = new List<Term>();
        var seen = new HashSet<Term>(ReferenceEqualityComparer.Instance);
        foreach (Term next in template.Next)
        {
            Collect(next, members, seen);
        }

        _transition = [.. members];
        _transitionBits = template.ParamBits;
        foreach (Term member in members)
        {
            _transitionBits = Bits.Union(_transitionBits, member.Support);
        }

        if (members.Count == 0)
        {
            _transitionIs = [null];
            return;
        }

        Term joint = factory.Tuple(_transition);
        _transitions = inference.LawOf(joint);
        if (_transitions.Values.Length > MaxTransitionClasses)
        {
            throw new LoopChainDeclinedException("an iteration moves the state in too many ways");
        }

        var positions = new Dictionary<ExprValue, int>(_transitions.Values.Length, V.ValueComparer.Instance);
        for (var i = 0; i < _transitions.Values.Length; i++)
        {
            positions[_transitions.Values[i]] = i;
        }

        Term position = factory.Index(joint, positions, true);
        _transitionIs = new Term?[_transitions.Values.Length];
        for (var i = 0; i < _transitionIs.Length; i++)
        {
            _transitionIs[i] = factory.Cmp(CompareOp.Eq, position, factory.Const(ExprValue.Number(i)));
        }
    }

    private int Classes => _transitionIs.Length;

    /// <summary>How many distinct states the loop was seen in.</summary>
    public int States => _kernels.Count;

    private void Collect(Term term, List<Term> members, HashSet<Term> seen)
    {
        if (term is ConstTerm or ParamTerm)
        {
            return;
        }

        if (!Bits.Intersects(term.Support, _f.ParamSlots))
        {
            if (seen.Add(term))
            {
                members.Add(term);
            }

            return;
        }

        if (!seen.Add(term))
        {
            return;
        }

        var children = new List<Term>();
        TermFactory.Children(term, children);
        foreach (Term child in children)
        {
            Collect(child, members, seen);
        }
    }

    private int PairIndex(int a, int b) => a <= b
        ? a * _accumulators - a * (a - 1) / 2 + (b - a)
        : PairIndex(b, a);

    private static Rational AsRational(Law law, int index) => new(law.Mass[index], law.Total);

    private static bool Sound(ExprValue value) => value.Kind == ExprType.Null ||
                                                  (value.Kind == ExprType.Array && !V.IsErr(value.ArrayValue![0]));

    private Kernel KernelOf(ExprValue state)
    {
        if (_kernels.TryGetValue(state, out Kernel? known))
        {
            return known;
        }

        IReadOnlyList<ExprValue> values = state.ArrayValue!;
        var replace = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < values.Count; i++)
        {
            ParamTerm parameter = _template.StateParams[i];
            ExprValue value = values[i];
            // The pass was simplified under what each parameter declares; a value outside that would make it unsound.
            if ((V.IsErr(value) && !parameter.CanErr) || (!V.IsErr(value) && (V.KindBit(value) & parameter.Kinds) == 0)
                                                      || (parameter.Vals is { } declared && !V.IsErr(value) &&
                                                          !declared.Contains(value, V.ValueComparer.Instance)))
            {
                throw new LoopChainDeclinedException($"field '{_template.StateKeys[i]}' leaves its declared values");
            }

            replace[parameter] = _f.Const(value);
        }

        var memo = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);

        Term At(Term term)
        {
            return _inference.Substitute(term, replace, _template.ParamBits, memo);
        }

        var kernel = new Kernel();
        _f.Budget.Tick(values.Count + 1);
        if (_template.Stop is { } stop)
        {
            if (At(stop) is not ConstTerm { Value.Kind: ExprType.Boolean } decided)
            {
                throw new LoopChainDeclinedException("the stop condition fails in a reachable state");
            }

            kernel.Stopped = decided.Value.BoolValue;
        }

        if (kernel.Stopped)
        {
            if (_template.ExitReason is { } reason)
            {
                if (At(reason) is not ConstTerm { Value.Kind: ExprType.String } named ||
                    !Reasons.Contains(named.Value.StringValue))
                {
                    throw new LoopChainDeclinedException(
                        "the exit reason is not a classification in a reachable state");
                }

                kernel.Reason = named.Value.StringValue!;
            }

            return _kernels[state] = kernel;
        }

        // No statement of the body may fail on an outcome this state can meet.
        foreach (Obligation obligation in _template.Obligations)
        {
            Term result = At(obligation.Result);
            if (!result.CanErr || _inference.LawOf(result).ErrMass.IsZero)
            {
                continue;
            }

            Term guarded = _f.If(At(obligation.Guard), _f.Tuple([result]), _f.NullT);
            if (!_inference.LawOf(guarded).Values.All(Sound))
            {
                throw new LoopChainDeclinedException($"{obligation.Where} can fail in a reachable state");
            }
        }

        kernel.Targets = new ExprValue[Classes];
        kernel.Probability = new Rational[Classes];
        var target = new ExprValue[values.Count];
        var memoAt = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        for (var z = 0; z < Classes; z++)
        {
            kernel.Probability[z] = _transitions is null ? Rational.One : AsRational(_transitions, z);
            if (_transitions is not null)
            {
                IReadOnlyList<ExprValue> drawn = _transitions.Values[z].ArrayValue!;
                for (var i = 0; i < _transition.Length; i++)
                {
                    replace[_transition[i]] = _f.Const(drawn[i]);
                }
            }

            memoAt.Clear();
            for (var i = 0; i < target.Length; i++)
            {
                if (_inference.Substitute(_template.Next[i], replace, _transitionBits, memoAt) is not ConstTerm next)
                {
                    throw new LoopChainDeclinedException("the next state is not decided by the state and the draws");
                }

                target[i] = next.Value;
            }

            kernel.Targets[z] = ExprValue.Array((ExprValue[])target.Clone());
        }

        foreach (Term member in _transition)
        {
            replace.Remove(member);
        }

        _f.Budget.Tick((long)Classes * (values.Count + 1));
        var deltas = new Term[_accumulators];
        kernel.First = new Rational[_accumulators][];
        kernel.Zero = new Rational[_accumulators][];
        kernel.Bounds = new Inference.Bound?[_accumulators];
        kernel.Second = new Rational[_pairs][];
        for (var a = 0; a < _accumulators; a++)
        {
            deltas[a] = At(_template.Deltas[a]);
            kernel.First[a] = FirstOf(deltas[a]);
            (kernel.Zero[a], kernel.Bounds[a]) = SignOf(deltas[a]);
        }

        for (var a = 0; a < _accumulators; a++)
            for (var b = a; b < _accumulators; b++)
            {
                kernel.Second[PairIndex(a, b)] = SecondOf(deltas[a], deltas[b]);
            }

        return _kernels[state] = kernel;
    }

    /// <summary>E[delta; transition z] for every z. A delta that reads no parameter is the same term in every state.</summary>
    private Rational[] FirstOf(Term delta)
    {
        if (_first.TryGetValue(delta.Id, out Rational[]? known))
        {
            return known;
        }

        var result = new Rational[Classes];
        if (_f.Fault == StructuredFault.ChainRewardsUnconditional)
        {
            Rational mean = _inference.Expect(null, delta);
            for (var z = 0; z < Classes; z++)
            {
                result[z] = mean * (_transitions is null ? Rational.One : AsRational(_transitions, z));
            }

            return _first[delta.Id] = result;
        }

        for (var z = 0; z < Classes; z++)
        {
            result[z] = _inference.Expect(_transitionIs[z], delta);
        }

        return _first[delta.Id] = result;
    }

    private Rational[] SecondOf(Term a, Term b)
    {
        (int, int) key = a.Id <= b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
        if (_second.TryGetValue(key, out Rational[]? known))
        {
            return known;
        }

        var result = new Rational[Classes];
        for (var z = 0; z < Classes; z++)
        {
            result[z] = _inference.Product(_transitionIs[z], a, b);
        }

        return _second[key] = result;
    }

    /// <summary>P(delta = 0; transition z) for a delta that is never negative, and the delta's proven range.</summary>
    private (Rational[]? Zero, Inference.Bound? Bound) SignOf(Term delta)
    {
        if (_signs.TryGetValue(delta.Id, out (Rational[]?, Inference.Bound?) known))
        {
            return known;
        }

        Inference.Bound? bound = _inference.BoundOf(delta);
        Rational[]? zero = null;
        if (bound is { } proven && ExprValue.CompareNumbers(proven.Lo, V.Zero) >= 0)
        {
            zero = new Rational[Classes];
            Term positive = _inference.Positive(delta);
            if (_f.Fault == StructuredFault.ChainZeroMassUnconditional)
            {
                Rational none = Rational.One - _inference.Probability(positive);
                for (var z = 0; z < Classes; z++)
                {
                    zero[z] = none * (_transitions is null ? Rational.One : AsRational(_transitions, z));
                }
            }
            else
            {
                for (var z = 0; z < Classes; z++)
                {
                    zero[z] = _transitionIs[z] is { } at
                        ? _inference.Probability(_f.And(at, _f.Not(positive)))
                        : Rational.One - _inference.Probability(positive);
                }
            }
        }

        return _signs[delta.Id] = (zero, bound);
    }

    // ── Integers over one denominator ────────────────────────────────────

    private void Widen(Rational value)
    {
        if (!value.Denominator.IsOne && !(_denominator % value.Denominator).IsZero)
        {
            _denominator = Rational.Lcm(_denominator, value.Denominator);
            _version++;
        }
    }

    private void Register(Kernel kernel)
    {
        if (kernel.Stopped)
        {
            return;
        }

        foreach (Rational value in kernel.Probability)
        {
            Widen(value);
        }

        foreach (Rational[] row in kernel.First)
            foreach (Rational value in row)
            {
                Widen(value);
            }

        foreach (Rational[] row in kernel.Second)
            foreach (Rational value in row)
            {
                Widen(value);
            }

        foreach (Rational[]? row in kernel.Zero)
        {
            if (row is null)
            {
                continue;
            }

            foreach (Rational value in row)
            {
                Widen(value);
            }
        }
    }

    private BigInteger Scale(Rational value) => value.Numerator * (_denominator / value.Denominator);

    private BigInteger[] Scale(Rational[] values)
    {
        var scaled = new BigInteger[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            scaled[i] = Scale(values[i]);
        }

        return scaled;
    }

    private void Rescale(Kernel kernel)
    {
        if (kernel.Stopped || kernel.Version == _version)
        {
            return;
        }

        kernel.Version = _version;
        kernel.ScaledProbability = Scale(kernel.Probability);
        kernel.ScaledFirst = kernel.First.Select(Scale).ToArray();
        kernel.ScaledSecond = kernel.Second.Select(Scale).ToArray();
        kernel.ScaledZero = kernel.Zero.Select(row => row is null ? null : Scale(row)).ToArray();
    }

    private static ExprValue Least(ExprValue a, ExprValue b) => ExprValue.CompareNumbers(a, b) <= 0 ? a : b;
    private static ExprValue Most(ExprValue a, ExprValue b) => ExprValue.CompareNumbers(a, b) >= 0 ? a : b;

    private void Hull(Cell into, ExprValue[] lo, ExprValue[] hi)
    {
        for (var a = 0; a < _accumulators; a++)
        {
            into.Lo[a] = into.Ranged ? Least(into.Lo[a], lo[a]) : lo[a];
            into.Hi[a] = into.Ranged ? Most(into.Hi[a], hi[a]) : hi[a];
        }

        into.Ranged = true;
    }

    /// <summary>Run the loop from one entry state: at most Cap iterations, each one transfer step.</summary>
    public LoopExits Run(ExprValue entry)
    {
        var running = new Dictionary<ExprValue, Cell>(V.ValueComparer.Instance);
        var done = new Dictionary<ExprValue, Cell>(V.ValueComparer.Instance);
        var first = new Cell(_accumulators, _pairs) { Mass = BigInteger.One };
        Array.Fill(first.Lo, V.Zero);
        Array.Fill(first.Hi, V.Zero);
        first.Ranged = true;
        for (var a = 0; a < _accumulators; a++)
        {
            first.Zero[a] = BigInteger.One;
        }

        running[entry] = first;
        BigInteger total = BigInteger.One;
        var steps = _template.Cap + (_f.Fault == StructuredFault.ChainCapOffByOne ? 1 : 0);
        var lo = new ExprValue[_accumulators];
        var hi = new ExprValue[_accumulators];
        Rational pruned = Rational.Zero;
        long spent = 0;
        for (var step = 0; step < steps && running.Count > 0; step++)
        {
            var kernels = new List<(ExprValue State, Cell Cell, Kernel Kernel)>(running.Count);
            foreach ((ExprValue state, Cell cell) in running)
            {
                Kernel kernel = KernelOf(state);
                Register(kernel);
                kernels.Add((state, cell, kernel));
            }

            if (_template.PruneBelow is { } epsilon && (total.GetBitLength() > _template.PruneAboveBits ||
                                                        spent > _template.PruneAboveWork))
            {
                // What would run this iteration; the rest stops here whatever is decided.
                BigInteger alive = BigInteger.Zero;
                foreach ((_, Cell cell, Kernel kernel) in kernels)
                {
                    if (!kernel.Stopped)
                    {
                        alive += cell.Mass;
                    }
                }

                if (alive * epsilon.Denominator < epsilon.Numerator * total)
                {
                    running = new Dictionary<ExprValue, Cell>(V.ValueComparer.Instance);
                    foreach ((ExprValue state, Cell cell, Kernel kernel) in kernels)
                    {
                        if (kernel.Stopped)
                        {
                            Absorb(done, Exit(state, kernel.Reason), cell);
                        }
                        else
                        {
                            running[state] = cell;
                        }
                    }

                    pruned = new Rational(alive, total);
                    break;
                }
            }

            BigInteger scale = _denominator;
            // A unit of work is some tens of nanoseconds; a multiply-and-add on numbers this long costs one per 256 bits.
            var words = 1 + total.GetBitLength() / 256;
            var work = ((long)kernels.Count * Classes * (1 + _accumulators + _pairs) + done.Count) * words;
            spent += work;
            _f.Budget.Tick(work);
            var next = new Dictionary<ExprValue, Cell>(running.Count + 8, V.ValueComparer.Instance);
            foreach (Cell cell in done.Values)
            {
                Multiply(cell, scale);
            }

            foreach ((ExprValue state, Cell cell, Kernel kernel) in kernels)
            {
                if (kernel.Stopped)
                {
                    Multiply(cell, scale);
                    Absorb(done, Exit(state, kernel.Reason), cell);
                    continue;
                }

                Rescale(kernel);
                for (var a = 0; a < _accumulators; a++)
                {
                    if (kernel.Bounds[a] is { } bound)
                    {
                        lo[a] = ExprValue.Add(cell.Lo[a], bound.Lo);
                        hi[a] = ExprValue.Add(cell.Hi[a], bound.Hi);
                        Den[a] = Rational.Lcm(Den[a], bound.Den);
                    }
                    else
                    {
                        Bounded[a] = false;
                        (lo[a], hi[a]) = (cell.Lo[a], cell.Hi[a]);
                    }

                    Signed[a] &= kernel.Zero[a] is not null;
                }

                for (var z = 0; z < Classes; z++)
                {
                    BigInteger p = kernel.ScaledProbability[z];
                    if (p.IsZero)
                    {
                        continue;
                    }

                    if (!next.TryGetValue(kernel.Targets[z], out Cell? into))
                    {
                        next[kernel.Targets[z]] = into = new Cell(_accumulators, _pairs);
                    }

                    Hull(into, lo, hi);
                    into.Mass += cell.Mass * p;
                    for (var a = 0; a < _accumulators; a++)
                    {
                        into.First[a] += cell.First[a] * p + cell.Mass * kernel.ScaledFirst[a][z];
                        if (kernel.ScaledZero[a] is { } zero)
                        {
                            into.Zero[a] += cell.Zero[a] * zero[z];
                        }

                        for (var b = a; b < _accumulators; b++)
                        {
                            var pair = PairIndex(a, b);
                            BigInteger sum = cell.Second[pair] * p + cell.Mass * kernel.ScaledSecond[pair][z];
                            if (_f.Fault != StructuredFault.ChainCrossTermDropped)
                            {
                                sum += cell.First[a] * kernel.ScaledFirst[b][z] +
                                       cell.First[b] * kernel.ScaledFirst[a][z];
                            }

                            into.Second[pair] += sum;
                        }
                    }
                }
            }

            total *= scale;
            running = next;
        }

        // What is still running after the last iteration leaves at the cap, without the stop condition being asked.
        foreach ((ExprValue state, Cell cell) in running)
        {
            Absorb(done, Exit(state, "modelLimit"), cell);
        }

        LoopExits exits = Finish(done, total);
        exits.Pruned = pruned;
        return exits;
    }

    private ExprValue Exit(ExprValue state, string reason)
    {
        var values = new ExprValue[state.ArrayValue!.Count + 1];
        for (var i = 0; i < values.Length - 1; i++)
        {
            values[i] = _template.Forgotten[i] ? ExprValue.Null : state.ArrayValue[i];
        }

        values[^1] = ExprValue.String(reason);
        return ExprValue.Array(values);
    }

    private void Multiply(Cell cell, BigInteger scale)
    {
        if (scale.IsOne)
        {
            return;
        }

        cell.Mass *= scale;
        for (var a = 0; a < _accumulators; a++)
        {
            cell.First[a] *= scale;
            cell.Zero[a] *= scale;
        }

        for (var pair = 0; pair < _pairs; pair++)
        {
            cell.Second[pair] *= scale;
        }
    }

    private void Absorb(Dictionary<ExprValue, Cell> done, ExprValue key, Cell cell)
    {
        if (!done.TryGetValue(key, out Cell? into))
        {
            done[key] = cell;
            return;
        }

        Hull(into, cell.Lo, cell.Hi);
        into.Mass += cell.Mass;
        for (var a = 0; a < _accumulators; a++)
        {
            into.First[a] += cell.First[a];
            into.Zero[a] += cell.Zero[a];
        }

        for (var pair = 0; pair < _pairs; pair++)
        {
            into.Second[pair] += cell.Second[pair];
        }
    }

    private LoopExits Finish(Dictionary<ExprValue, Cell> done, BigInteger total)
    {
        // Classes in a fixed order, so that the variables built over them do not depend on dictionary order.
        List<KeyValuePair<ExprValue, Cell>> classes = done.Where(entry => !entry.Value.Mass.IsZero).ToList();
        classes.Sort(static (a, b) => V.Order(a.Key, b.Key));
        BigInteger divisor = total;
        foreach (KeyValuePair<ExprValue, Cell> entry in classes)
        {
            divisor = BigInteger.GreatestCommonDivisor(divisor, entry.Value.Mass);
        }

        var exits = new LoopExits
        {
            Classes = new ExprValue[classes.Count][],
            Mass = new BigInteger[classes.Count],
            Total = total / divisor,
            Mean = new Rational[classes.Count][],
            Second = new Rational[classes.Count][],
            Zero = new Rational?[classes.Count][]
        };
        for (var c = 0; c < classes.Count; c++)
        {
            Cell cell = classes[c].Value;
            exits.Classes[c] = [.. classes[c].Key.ArrayValue!];
            exits.Mass[c] = cell.Mass / divisor;
            exits.Mean[c] = new Rational[_accumulators];
            exits.Zero[c] = new Rational?[_accumulators];
            exits.Second[c] = new Rational[_pairs];
            for (var a = 0; a < _accumulators; a++)
            {
                exits.Mean[c][a] = new Rational(cell.First[a], cell.Mass);
                exits.Zero[c][a] = Signed[a] ? new Rational(cell.Zero[a], cell.Mass) : null;
                Lo[a] = Least(Lo[a], cell.Lo[a]);
                Hi[a] = Most(Hi[a], cell.Hi[a]);
            }

            for (var pair = 0; pair < _pairs; pair++)
            {
                exits.Second[c][pair] = new Rational(cell.Second[pair], cell.Mass);
            }
        }

        _f.Budget.Tick(classes.Count * (1L + _accumulators + _pairs) * (1 + total.GetBitLength() / 256));
        return exits;
    }

    // ── Terms for what follows the loop ──────────────────────────────────

    /// <summary>
    ///     The loop's outcome as terms. Each entry state has a variable of its own over that entry's exit classes;
    ///     the entry's position picks among them, so the outcome has the right law given the entry and is tied to
    ///     the rest of the round through the entry alone.
    /// </summary>
    public (Term[] State, Term Reason, Term[] Amounts) Materialize(Term entry, ExprValue[] entries, LoopExits[] exits,
        string name, bool total)
    {
        Term? position = null;
        if (entries.Length > 1)
        {
            var positions = new Dictionary<ExprValue, int>(entries.Length, V.ValueComparer.Instance);
            for (var v = 0; v < entries.Length; v++)
            {
                positions[entries[v]] = v;
            }

            position = _f.Index(entry, positions, total);
        }

        var variables = new Rv[entries.Length];
        for (var v = 0; v < entries.Length; v++)
        {
            variables[v] = _f.NewRv($"{name}/exit{v}", exits[v].Mass, true);
        }

        Term Column(Func<LoopExits, int, ExprValue> cell)
        {
            var columns = new Term[entries.Length];
            for (var v = 0; v < entries.Length; v++)
            {
                var values = new ExprValue[exits[v].Mass.Length];
                for (var c = 0; c < values.Length; c++)
                {
                    values[c] = cell(exits[v], c);
                }

                columns[v] = _f.Table(variables[v], values);
            }

            return position is null ? columns[0] : _f.Sel(position, columns);
        }

        static ExprValue Number(Rational value)
        {
            return ExprValue.Rational(value.Numerator, value.Denominator);
        }

        var fields = _template.StateKeys.Length;
        var state = new Term[fields];
        for (var i = 0; i < fields; i++)
        {
            var field = i;
            state[i] = Column((exit, c) => exit.Classes[c][field]);
        }

        Term reason = Column((exit, c) => exit.Classes[c][fields]);
        var amounts = new Term[_accumulators];
        if (_accumulators == 0)
        {
            return (state, reason, amounts);
        }

        var slot = _f.NewLoopSlot(name);
        var moments = new MomentTerm?[_accumulators];
        for (var a = 0; a < _accumulators; a++)
        {
            var index = a;
            Term mean = Column((exit, c) => Number(exit.Mean[c][index]));
            // An amount with no variance left given entry and exit is that conditional mean itself, an ordinary term.
            var settled = exits.All(exit => Enumerable.Range(0, exit.Mass.Length).All(c =>
                exit.Second[c][PairIndex(index, index)] == exit.Mean[c][index] * exit.Mean[c][index]));
            if (settled)
            {
                amounts[a] = mean;
                continue;
            }

            MomentTerm moment = _f.NewMoment(slot, a, mean);
            moment.Bounded = Bounded[a];
            (moment.Lo, moment.Hi, moment.Den) = (Lo[a], Hi[a], Den[a]);
            if (Signed[a])
            {
                moment.Positive = () => Positive(position, variables, exits, index, name);
            }

            moments[a] = moment;
            amounts[a] = moment;
        }

        for (var a = 0; a < _accumulators; a++)
        {
            if (moments[a] is not { } moment)
            {
                continue;
            }

            moment.Second = new Term[_accumulators];
            for (var b = 0; b < _accumulators; b++)
            {
                var pair = PairIndex(a, b);
                moment.Second[b] = Column((exit, c) => Number(exit.Second[c][pair]));
            }
        }

        return (state, reason, amounts);
    }

    private Term Positive(Term? position, Rv[] variables, LoopExits[] exits, int accumulator, string name)
    {
        var columns = new Term[exits.Length];
        for (var v = 0; v < exits.Length; v++)
        {
            LoopExits exit = exits[v];
            var classes = new Term[exit.Mass.Length];
            var ordinals = new ExprValue[classes.Length];
            for (var c = 0; c < classes.Length; c++)
            {
                ordinals[c] = ExprValue.Number(c);
                Rational zero = exit.Zero[c][accumulator]!.Value;
                classes[c] = zero.IsZero ? _f.TrueT
                    : zero == Rational.One ? _f.FalseT
                    : _f.Table(
                        _f.NewRv($"{name}/hit{accumulator}.{v}.{c}",
                            [zero.Numerator, zero.Denominator - zero.Numerator], true), [V.False, V.True]);
            }

            columns[v] = _f.Sel(_f.Table(variables[v], ordinals), classes);
        }

        return position is null ? columns[0] : _f.Sel(position, columns);
    }

    // ── The kernel of one state ──────────────────────────────────────────

    private sealed class Kernel
    {
        public Inference.Bound?[] Bounds = [];
        public Rational[][] First = [];
        public Rational[] Probability = [];
        public string Reason = "condition";
        public BigInteger[][] ScaledFirst = [], ScaledSecond = [];
        public BigInteger[] ScaledProbability = [];
        public BigInteger[]?[] ScaledZero = [];
        public Rational[][] Second = [];
        public bool Stopped;
        public ExprValue[] Targets = [];

        public int Version = -1;
        public Rational[]?[] Zero = [];
    }

    // ── The transfer iteration ───────────────────────────────────────────

    private sealed class Cell(int accumulators, int pairs)
    {
        public readonly BigInteger[] First = new BigInteger[accumulators];
        public readonly ExprValue[] Hi = new ExprValue[accumulators];
        public readonly ExprValue[] Lo = new ExprValue[accumulators];
        public readonly BigInteger[] Second = new BigInteger[pairs];
        public readonly BigInteger[] Zero = new BigInteger[accumulators];
        public BigInteger Mass;
        public bool Ranged;
    }
}
