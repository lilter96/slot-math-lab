using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  Inference — exact laws and expectations of terms
//
//  The law of a term is found without enumerating the joint outcomes of its
//  variables, by four exact reductions:
//
//   1. Independence. Sub-terms over disjoint sets of variables are independent
//      (variables are independent draws), so a term is cut at the highest set
//      of sub-terms that falls into two or more such groups. Each group is
//      replaced by one fresh variable carrying the group's joint law.
//   2. Lumping. That fresh variable has one outcome per distinct joint value,
//      not per outcome of the draws underneath: a reel of 85 stops seen
//      through one pay line becomes a variable of a dozen symbols.
//   3. Symmetry. Fresh variables are named by their law, so two terms that
//      differ only in which draws they read (two pay lines, two free spins in
//      the same state) become the same term and are computed once.
//   4. Conditioning. What remains is expanded on one variable at a time in
//      evaluation order; equal residual terms are found by hash-consing.
//
//  Sums of independent parts are convolved, and an expectation is taken by
//  linearity, so E[payout] never needs the law of the payout itself.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A finite distribution: Values[i] has probability Mass[i] / Total. Values are listed in a fixed order.</summary>
internal sealed class Law
{
    public int Hash;
    public int Id;

    /// <summary>The hash of each value, kept so that it is not computed again.</summary>
    public required int[] Keys;

    public required BigInteger[] Mass;
    public required BigInteger Total;
    public required ExprValue[] Values;

    public BigInteger ErrMass
    {
        get
        {
            BigInteger mass = BigInteger.Zero;
            for (var i = 0; i < Values.Length; i++)
            {
                if (V.IsErr(Values[i]))
                {
                    mass += Mass[i];
                }
            }

            return mass;
        }
    }

    public sealed class Comparer : IEqualityComparer<Law>
    {
        public int GetHashCode(Law law) => law.Hash;

        public bool Equals(Law? a, Law? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null || a.Hash != b.Hash || a.Values.Length != b.Values.Length || a.Total != b.Total)
            {
                return false;
            }

            for (var i = 0; i < a.Values.Length; i++)
            {
                if (a.Mass[i] != b.Mass[i] || !a.Values[i].Equals(b.Values[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

internal sealed class InferenceStats
{
    public long Laws, Decompositions, Expansions, Substitutions, Convolutions, PseudoVariables, Expectations;

    public long TransformConvolutions,
        DirectConvolutionPairs,
        LargestConvolutionSpan,
        LargestConvolutionCoefficientBits;
}

internal sealed partial class Inference(TermFactory f, bool transformConvolution = true)
{
    // Most laws are small: one table collects their masses, and larger ones get a table of their own.
    private const int SmallLaw = 1024;
    private const int JointOutcomes = 1 << 16;

    // ── Laws as values ───────────────────────────────────────────────────

    private static readonly Comparer<ExprValue> ByOrder = Comparer<ExprValue>.Create(V.Order);
    public readonly InferenceStats Stats = new();

    private readonly Dictionary<int, Bound?> _bounds = new();
    private readonly List<Term> _children = [];
    private readonly Dictionary<(string Guard, int Term), Rational> _expectations = new();
    private readonly Dictionary<int, int> _firstRv = new();

    // ── Probabilities and expectations ───────────────────────────────────

    private readonly Dictionary<string, Rational> _guardProbability = new();
    private readonly Dictionary<Law, Law> _interned = new(new Law.Comparer());
    private readonly Dictionary<int, Law> _laws = new();

    // ── Hit indicator ────────────────────────────────────────────────────

    private readonly Dictionary<int, Term> _positive = new();
    private readonly Dictionary<int, int> _positiveOf = new();

    // ── Second moments ───────────────────────────────────────────────────

    private readonly Dictionary<(string Guard, int X, int Y), Rational> _products = new();
    private readonly Dictionary<(int Law, int Ordinal), Rv> _pseudo = new();
    private Masses _masses = new();

    /// <summary>A table to collect masses in. It is filled and handed to Build before the next one is asked for.</summary>
    private Masses NewMasses(int capacity)
    {
        if (capacity > SmallLaw)
        {
            var large = new Masses();
            large.Reset(capacity);
            return large;
        }

        if (_masses.Count > SmallLaw)
        {
            _masses = new Masses();
        }

        _masses.Reset(capacity);
        return _masses;
    }

    private Law Build(Masses masses, BigInteger total)
    {
        // The canonical listing: by hash, and by the total order only among values whose hashes collide.
        // Comparing hashes is far cheaper than comparing values, which are often long tuples.
        var count = masses.Count;
        var values = new ExprValue[count];
        var mass = new BigInteger[count];
        var keys = masses.Keys.AsSpan(0, count).ToArray();
        var order = ArrayPool<int>.Shared.Rent(count);
        BigInteger divisor = total;
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            if (!divisor.IsOne)
            {
                divisor = BigInteger.GreatestCommonDivisor(divisor, masses.Mass[i]);
            }
        }

        Array.Sort(keys, order, 0, count);
        for (var i = 0; i < count; i++)
        {
            values[i] = masses.Values[order[i]];
            mass[i] = divisor.IsOne ? masses.Mass[order[i]] : masses.Mass[order[i]] / divisor;
        }

        ArrayPool<int>.Shared.Return(order);
        for (var start = 0; start < count;)
        {
            var end = start + 1;
            while (end < count && keys[end] == keys[start])
            {
                end++;
            }

            if (end - start > 1)
            {
                Array.Sort(values, mass, start, end - start, ByOrder);
            }

            start = end;
        }

        return Intern(values, mass, keys, divisor.IsOne ? total : total / divisor);
    }

    private Law Intern(ExprValue[] values, BigInteger[] mass, int[] keys, BigInteger total)
    {
        var hash = new HashCode();
        hash.Add(total);
        for (var i = 0; i < values.Length; i++)
        {
            hash.Add(keys[i]);
            hash.Add(mass[i]);
        }

        var law = new Law
        {
            Values = values,
            Mass = mass,
            Keys = keys,
            Total = total,
            Hash = hash.ToHashCode()
        };
        if (_interned.TryGetValue(law, out Law? existing))
        {
            return existing;
        }

        law.Id = _interned.Count;
        _interned.Add(law, law);
        f.Budget.Tick(values.Length);
        return law;
    }

    /// <summary>Machine words of a denominator: what one multiplication of masses over it costs, so that work tracks time.</summary>
    private static long Words(BigInteger total) => 1 + total.GetBitLength() / 64;

    /// <summary>The mixture Σ weight/totalWeight · law.</summary>
    private Law Mix(List<(BigInteger Weight, Law Law)> parts, BigInteger totalWeight)
    {
        if (parts.Count == 1)
        {
            return parts[0].Law;
        }

        BigInteger common = BigInteger.One;
        foreach ((BigInteger _, Law law) in parts)
        {
            if (!law.Total.IsOne)
            {
                common = Rational.Lcm(common, law.Total);
            }
        }

        var largest = 0;
        foreach ((BigInteger _, Law law) in parts)
        {
            largest = System.Math.Max(largest, law.Values.Length);
        }

        Masses masses = NewMasses(largest);
        foreach ((BigInteger weight, Law law) in parts)
        {
            BigInteger scale = law.Total == common ? weight : weight * (common / law.Total);
            f.Budget.Tick(law.Values.Length * Words(common));
            for (var i = 0; i < law.Values.Length; i++)
            {
                masses.Add(law.Values[i], law.Keys[i], law.Mass[i] * scale);
            }
        }

        return Build(masses, totalWeight * common);
    }

    /// <summary>The law of X + Y for independent numeric X and Y; an error on either side is an error.</summary>
    private Law Convolve(Law a, Law b)
    {
        Stats.Convolutions++;
        if (transformConvolution && TryTransformConvolution(a, b) is { } transformed)
        {
            return transformed;
        }

        Stats.DirectConvolutionPairs += (long)a.Values.Length * b.Values.Length;
        f.Budget.Tick((long)a.Values.Length * b.Values.Length * (Words(a.Total) + Words(b.Total) - 1));
        Masses masses = NewMasses(a.Values.Length + b.Values.Length);
        for (var i = 0; i < a.Values.Length; i++)
        {
            for (var j = 0; j < b.Values.Length; j++)
            {
                ExprValue value = V.IsErr(a.Values[i]) || V.IsErr(b.Values[j])
                    ? V.Err
                    : ExprValue.Add(a.Values[i], b.Values[j]);
                masses.Add(value, V.Hash(value), a.Mass[i] * b.Mass[j]);
            }
        }

        return Build(masses, a.Total * b.Total);
    }

    // ── The law of a term ────────────────────────────────────────────────

    public Law LawOf(Term term)
    {
        if (_laws.TryGetValue(term.Id, out Law? known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        Lawless(term);
        Stats.Laws++;
        Law law;
        switch (term)
        {
            case ConstTerm constant:
                law = Intern([constant.Value], [BigInteger.One], [V.Hash(constant.Value)], BigInteger.One);
                break;
            case TableTerm table:
                Masses masses = NewMasses(table.Vals?.Length ?? table.Values.Length);
                for (var i = 0; i < table.Values.Length; i++)
                {
                    masses.Add(table.Values[i], V.Hash(table.Values[i]), table.Rv.Weights[i]);
                }

                law = Build(masses, table.Rv.Total);
                break;
            case LinTerm lin:
                law = Independent(lin) ?? Decomposed(term);
                break;
            case OpTerm { Code: Op.Tuple } tuple when Tabulated(tuple) is { } joint:
                law = joint;
                break;
            default:
                law = Decomposed(term);
                break;
        }

        return _laws[term.Id] = law;
    }

    /// <summary>
    ///     The joint law of tables of one variable, read off its outcomes. The factory leaves such a tuple unfolded
    ///     when the variable has too many outcomes to tabulate, and taking it apart only finds the same tables again.
    /// </summary>
    private Law? Tabulated(OpTerm tuple)
    {
        Rv? variable = null;
        foreach (Term element in tuple.Args)
        {
            if (element is not TableTerm table || (variable ??= table.Rv) != table.Rv)
            {
                return null;
            }
        }

        if (variable is null)
        {
            return null;
        }

        f.Budget.Tick(variable.Count);
        Masses masses = NewMasses(variable.Count);
        for (var outcome = 0; outcome < variable.Count; outcome++)
        {
            var elements = new ExprValue[tuple.Args.Length];
            for (var k = 0; k < elements.Length; k++)
            {
                elements[k] = ((TableTerm)tuple.Args[k]).Values[outcome];
            }

            ExprValue value = ExprValue.ArrayOwned(elements, false);
            masses.Add(value, V.Hash(value), variable.Weights[outcome]);
        }

        return Build(masses, variable.Total);
    }

    /// <summary>Refuse a term that has no law: one over a loop's moments, a poisoned field or an open parameter.</summary>
    private void Lawless(Term term)
    {
        if (Bits.Intersects(term.Support, f.PoisonSlots))
        {
            throw new StructuredPoisonException("a field written inside a summarized loop is read after it");
        }

        if (Bits.Intersects(term.Support, f.MomentSlots))
        {
            throw new StructuredLawUnavailableException(
                "an amount accumulated by a long loop is known by its moments, not by its distribution");
        }

        if (Bits.Intersects(term.Support, f.ParamSlots))
        {
            throw new InvalidOperationException("The law of a term with an open parameter was asked for.");
        }
    }

    /// <summary>A sum whose summands fall into independent groups is the convolution of the groups' laws.</summary>
    private Law? Independent(LinTerm lin)
    {
        List<List<int>> groups = f.Fault == StructuredFault.DependentSumsConvolved
            ? Enumerable.Range(0, lin.Terms.Length).Select(i => new List<int>
            {
                i
            }).ToList()
            : Components(lin.Terms);
        if (groups.Count < 2)
        {
            return null;
        }

        Law law = LawOf(f.Const(lin.C0));
        foreach (List<int> group in groups)
        {
            var parts = new List<(ExprValue, Term)>(group.Count);
            foreach (var index in group)
            {
                parts.Add((lin.Coefs[index], lin.Terms[index]));
            }

            law = Convolve(law, LawOf(f.Lin(V.Zero, parts)));
        }

        return law;
    }

    /// <summary>Group terms that share a variable, directly or through other terms. Groups keep first-member order.</summary>
    private static List<List<int>> Components(IReadOnlyList<Term> terms)
    {
        var groups = new List<(ulong[] Support, List<int> Members)>();
        for (var i = 0; i < terms.Count; i++)
        {
            var support = terms[i].Support;
            var target = -1;
            for (var g = 0; g < groups.Count; g++)
            {
                if (!Bits.Intersects(groups[g].Support, support))
                {
                    continue;
                }

                if (target < 0)
                {
                    target = g;
                    groups[g].Members.Add(i);
                    groups[g] = (Bits.Union(groups[g].Support, support), groups[g].Members);
                }
                else
                {
                    groups[target].Members.AddRange(groups[g].Members);
                    groups[target] = (Bits.Union(groups[target].Support, groups[g].Support), groups[target].Members);
                    groups.RemoveAt(g--);
                }
            }

            if (target < 0)
            {
                groups.Add((support, [i]));
            }
        }

        var result = new List<List<int>>(groups.Count);
        foreach ((_, List<int> members) in groups)
        {
            members.Sort();
            result.Add(members);
        }

        result.Sort(static (a, b) => a[0].CompareTo(b[0]));
        return result;
    }

    private Law Decomposed(Term term)
    {
        Stats.Decompositions++;
        var frontier = new List<Term>();
        var member = new HashSet<Term>(ReferenceEqualityComparer.Instance);
        var opened = new HashSet<Term>(ReferenceEqualityComparer.Instance);

        void Open(Term node)
        {
            opened.Add(node);
            _children.Clear();
            TermFactory.Children(node, _children);
            foreach (Term child in _children)
            {
                if (child is not ConstTerm && !opened.Contains(child) && member.Add(child))
                {
                    frontier.Add(child);
                }
            }
        }

        Open(term);
        while (true)
        {
            f.Budget.Tick(frontier.Count + 1);
            if (frontier.Count == 0)
            {
                throw new StructuredDeclinedException("a term without variables did not reduce to a constant");
            }

            // One sub-term carries everything random about this term: its law is pushed through the rest.
            if (frontier.Count == 1)
            {
                return Through(term, frontier[0]);
            }

            if (Components(frontier).Count > 1)
            {
                break;
            }

            // Still one group: open a sub-term. One that spans every variable has to be opened whatever the cut;
            // the latest such term cannot be below another, so opening it first keeps the cut as high as possible.
            Term? pick = null;
            var pickFull = false;
            var pickSize = 0;
            foreach (Term node in frontier)
            {
                if (node is TableTerm)
                {
                    continue;
                }

                var full = Bits.IsSubset(term.Support, node.Support);
                var size = full ? 0 : Bits.Count(node.Support);
                if (pick is null || (full && !pickFull) ||
                    (full == pickFull && (size > pickSize || (size == pickSize && node.Id > pick.Id))))
                {
                    (pick, pickFull, pickSize) = (node, full, size);
                }
            }

            if (pick is null)
            {
                break; // only tables of one variable are left
            }

            frontier.Remove(pick);
            member.Remove(pick);
            Open(pick);
        }

        // Frontier members in evaluation order, so that two terms of the same shape list them alike.
        var ordered = new List<Term>(frontier.Count);
        var visited = new HashSet<Term>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Term>();
        stack.Push(term);
        var scratch = new List<Term>();
        while (stack.Count > 0)
        {
            Term node = stack.Pop();
            if (node is ConstTerm || !visited.Add(node))
            {
                continue;
            }

            if (member.Contains(node))
            {
                ordered.Add(node);
                continue;
            }

            scratch.Clear();
            TermFactory.Children(node, scratch);
            for (var i = scratch.Count - 1; i >= 0; i--)
            {
                stack.Push(scratch[i]);
            }
        }

        var replace = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        var affected = Bits.Empty;
        var ordinals = new Dictionary<int, int>();
        List<List<int>> groups = Components(ordered);

        // A group that is already the tables of one variable standing for its law, none of whose outcomes merge, has
        // nothing structural left. A group of several terms over several variables whose joint law could be as large
        // as the product of theirs is left alone as well: a pay chained along the reels next to a count summed over
        // them is such a group, and conditioning walks it reel by reel where the joint law would hold every reel
        // at once.
        var parts = new Term[groups.Count][];
        var settled = new bool[groups.Count];
        var lumped = new bool[groups.Count];
        var open = false;
        var wide = false;
        for (var g = 0; g < groups.Count; g++)
        {
            parts[g] = groups[g].Select(index => ordered[index]).ToArray();
            settled[g] = Named(parts[g]);
            lumped[g] = !settled[g] && Lumpable(parts[g]);
            open |= !settled[g];
            wide |= !settled[g] && !lumped[g];
        }

        // Naming settled variables again could only exchange two of equal law, and a term in which two such
        // variables do not appear alike would be renamed back and forth for ever.
        if (!open)
        {
            return Expanded(term);
        }

        var taken = new HashSet<Rv>();
        for (var g = 0; g < groups.Count; g++)
        {
            Term[] elements = parts[g];
            if (wide && !lumped[g])
            {
                continue;
            }

            Term joint = elements.Length == 1 ? elements[0] : f.Tuple(elements);
            Law law = LawOf(joint);
            Rv variable;
            if (f.Fault == StructuredFault.PseudoVariablesShared)
            {
                variable = Pseudo(law, 0);
            }
            else if (wide)
            {
                // Some variables keep their names here, so a new one takes the first name of its law not in use.
                var ordinal = 0;
                while (Bits.Has(term.Support, (variable = Pseudo(law, ordinal)).Id) || !taken.Add(variable))
                {
                    ordinal++;
                }
            }
            else
            {
                var ordinal = ordinals.GetValueOrDefault(law.Id);
                ordinals[law.Id] = ordinal + 1;
                variable = Pseudo(law, ordinal);
            }

            for (var k = 0; k < elements.Length; k++)
            {
                ExprValue[] column = f.Rent(law.Values.Length);
                if (elements.Length == 1)
                {
                    law.Values.CopyTo(column, 0);
                }
                else
                {
                    for (var i = 0; i < column.Length; i++)
                    {
                        column[i] = law.Values[i].ArrayValue![k];
                    }
                }

                replace[elements[k]] = f.Table(variable, column);
                affected = Bits.Union(affected, elements[k].Support);
            }
        }

        if (replace.Count == 0)
        {
            return Expanded(term);
        }

        Term renamed = Rebuild(term,
            new Context(null, 0, replace, affected, new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance)));
        // Already written over variables named by their laws: nothing structural is left, so condition.
        return renamed == term ? Expanded(term) : LawOf(renamed);
    }

    /// <summary>The terms are tables of one variable that stands for their joint law, none of whose outcomes merge.</summary>
    private bool Named(Term[] elements)
    {
        Rv? variable = null;
        foreach (Term element in elements)
        {
            if (element is not TableTerm { Rv.Lumped: true } table || (variable ??= table.Rv) != table.Rv)
            {
                return false;
            }
        }

        return LawOf(elements.Length == 1 ? elements[0] : f.Tuple(elements)).Values.Length == variable!.Count;
    }

    /// <summary>
    ///     Whether the joint law of a group is worth one variable: always for one term or for tables of one variable,
    ///     otherwise only when neither its variables' outcomes nor its terms' values can combine into too many.
    /// </summary>
    private bool Lumpable(Term[] elements)
    {
        if (elements.Length == 1)
        {
            return true;
        }

        var support = Bits.Empty;
        var single = true;
        double values = 1;
        foreach (Term element in elements)
        {
            single = single && element is TableTerm table && Bits.IsSubset(support, table.Support);
            support = Bits.Union(support, element.Support);
            values *= element.Vals?.Length ??
                      (element is TableTerm tabulated ? tabulated.Rv.Count : double.PositiveInfinity);
        }

        if (single || values <= JointOutcomes)
        {
            return true;
        }

        double outcomes = 1;
        foreach (var id in Bits.Enumerate(support))
        {
            if ((outcomes *= f.Rvs[id].Count) > JointOutcomes)
            {
                return false;
            }
        }

        return true;
    }

    private Rv Pseudo(Law law, int ordinal)
    {
        if (_pseudo.TryGetValue((law.Id, ordinal), out Rv? variable))
        {
            return variable;
        }

        Stats.PseudoVariables++;
        return _pseudo[(law.Id, ordinal)] = f.NewRv($"law{law.Id}.{ordinal}", law.Mass, true);
    }

    /// <summary>The law of a term that is a function of one sub-term: push that sub-term's law through.</summary>
    private Law Through(Term term, Term module)
    {
        Law inner = LawOf(module);
        var memo = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        var parts = new List<(BigInteger, Law)>(inner.Values.Length);
        var replace = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < inner.Values.Length; i++)
        {
            memo.Clear();
            replace[module] = f.Const(inner.Values[i]);
            Stats.Substitutions++;
            parts.Add((inner.Mass[i], LawOf(Rebuild(term, new Context(null, 0, replace, module.Support, memo)))));
        }

        return Mix(parts, inner.Total);
    }

    /// <summary>Condition on the first variable the term evaluates and mix the laws of the residual terms.</summary>
    private Law Expanded(Term term)
    {
        Stats.Expansions++;
        Rv variable = f.Rvs[FirstRv(term)];
        f.Budget.Tick(variable.Count);
        var weights = new Dictionary<Term, BigInteger>(ReferenceEqualityComparer.Instance);
        var order = new List<Term>();
        var memo = new Dictionary<Term, Term>(ReferenceEqualityComparer.Instance);
        var affected = Bits.Single(variable.Id);
        for (var outcome = 0; outcome < variable.Count; outcome++)
        {
            memo.Clear();
            Stats.Substitutions++;
            Term residual = Rebuild(term, new Context(variable, outcome, null, affected, memo));
            if (weights.TryGetValue(residual, out BigInteger weight))
            {
                weights[residual] = weight + variable.Weights[outcome];
            }
            else
            {
                weights[residual] = variable.Weights[outcome];
                order.Add(residual);
            }
        }

        var parts = new List<(BigInteger, Law)>(order.Count);
        foreach (Term residual in order)
        {
            parts.Add((weights[residual], LawOf(residual)));
        }

        return Mix(parts, variable.Total);
    }

    private int FirstRv(Term term)
    {
        if (term is TableTerm table)
        {
            return table.Rv.Id;
        }

        if (term is ConstTerm)
        {
            return -1;
        }

        if (_firstRv.TryGetValue(term.Id, out var known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        var children = new List<Term>();
        TermFactory.Children(term, children);
        var first = -1;
        foreach (Term child in children)
        {
            if ((first = FirstRv(child)) >= 0)
            {
                break;
            }
        }

        return _firstRv[term.Id] = first;
    }

    private Term Rebuild(Term term, in Context context)
    {
        if (term is ConstTerm || !Bits.Intersects(term.Support, context.Affected))
        {
            return term;
        }

        if (context.Replace is not null && context.Replace.TryGetValue(term, out Term? replacement))
        {
            return replacement;
        }

        if (term is TableTerm table)
        {
            return table.Rv == context.Variable ? f.Const(table.Values[context.Outcome]) : term;
        }

        if (term is ParamTerm)
        {
            return term;
        }

        if (term is MomentTerm)
        {
            throw new StructuredLawUnavailableException(
                "an amount accumulated by a long loop is known by its moments, not by its distribution");
        }

        if (context.Memo.TryGetValue(term, out Term? done))
        {
            return done;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();

        Term result;
        switch (term)
        {
            case LinTerm lin:
            {
                var parts = new (ExprValue, Term)[lin.Terms.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = (lin.Coefs[i], Rebuild(lin.Terms[i], context));
                }

                result = f.Lin(lin.C0, parts);
                break;
            }
            case IfTerm conditional:
            {
                // Lazy: once the condition is known only the branch it takes is rebuilt.
                Term condition = Rebuild(conditional.C, context);
                result = condition is ConstTerm known
                    ? known.Value.Kind == ExprType.Boolean
                        ? Rebuild(known.Value.BoolValue ? conditional.A : conditional.B, context)
                        : f.ErrT
                    : f.If(condition, Rebuild(conditional.A, context), Rebuild(conditional.B, context));
                break;
            }
            case LogicTerm logic:
            {
                Term left = Rebuild(logic.A, context);
                result = left is ConstTerm known && (known.IsErr ||
                                                     (known.Value.Kind == ExprType.Boolean &&
                                                      known.Value.BoolValue == logic.IsOr))
                    ? left
                    : f.Logic(logic.IsOr, left, Rebuild(logic.B, context));
                break;
            }
            case SelTerm select:
            {
                Term selector = Rebuild(select.Selector, context);
                if (selector is ConstTerm chosen)
                {
                    result = chosen.Value.IsInlineInteger &&
                             (ulong)chosen.Value.InlineInteger < (ulong)select.Children.Length
                        ? Rebuild(select.Children[chosen.Value.InlineInteger], context)
                        : f.ErrT;
                    break;
                }

                var children = new Term[select.Children.Length];
                for (var i = 0; i < children.Length; i++)
                {
                    children[i] = Rebuild(select.Children[i], context);
                }

                result = f.Sel(selector, children);
                break;
            }
            default:
            {
                var op = (OpTerm)term;
                var args = new Term[op.Args.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = Rebuild(op.Args[i], context);
                }

                result = op.Code switch
                {
                    Op.Arith => f.Arith((BinaryOp)op.Aux, args[0], args[1]),
                    Op.Call => f.Call((string)op.Payload!, args),
                    _ => f.MakeOp(op.Code, op.Aux, op.Payload, op.Names, args)
                };
                break;
            }
        }

        return context.Memo[term] = result;
    }

    /// <summary>The term with sub-terms replaced; <paramref name="affected" /> covers the variables of what is replaced.</summary>
    public Term Substitute(Term term, Dictionary<Term, Term> replace, ulong[] affected, Dictionary<Term, Term> memo) =>
        Rebuild(term, new Context(null, 0, replace, affected, memo));

    /// <summary>E[term] of a numeric term. Declines when some outcome makes the term fail or non-numeric.</summary>
    public Rational Expect(Term term) => Expect([], "", term);

    /// <summary>E[term · 1{condition}]; a null condition is "always".</summary>
    public Rational Expect(Term? condition, Term term)
    {
        if (condition is null)
        {
            return Expect([], "", term);
        }

        return With([], condition, false) is { } guard ? Expect(guard, Key(guard), term) : Rational.Zero;
    }

    /// <summary>E[x · y · 1{condition}]; a null condition is "always".</summary>
    public Rational Product(Term? condition, Term x, Term y)
    {
        if (condition is null)
        {
            return Product([], "", x, y);
        }

        return With([], condition, false) is { } guard ? Product(guard, Key(guard), x, y) : Rational.Zero;
    }

    public Rational Mean(Law law)
    {
        BigInteger sum = BigInteger.Zero;
        BigInteger denominator = BigInteger.One;
        for (var i = 0; i < law.Values.Length; i++)
        {
            ExprValue value = law.Values[i];
            if (V.IsErr(value))
            {
                throw new StructuredDeclinedException("an expression can fail on a reachable outcome");
            }

            if (value.Kind != ExprType.Number)
            {
                throw new StructuredDeclinedException("an amount is not numeric on a reachable outcome");
            }

            (BigInteger numerator, BigInteger valueDenominator) = (value.NumberNumerator, value.NumberDenominator);
            if (valueDenominator == denominator)
            {
                sum += numerator * law.Mass[i];
            }
            else
            {
                BigInteger common = Rational.Lcm(denominator, valueDenominator);
                sum = sum * (common / denominator) + numerator * law.Mass[i] * (common / valueDenominator);
                denominator = common;
            }
        }

        return new Rational(sum, denominator * law.Total);
    }

    /// <summary>P(term is true) of a Boolean term.</summary>
    public Rational Probability(Term term)
    {
        Law law = LawOf(term);
        BigInteger mass = BigInteger.Zero;
        for (var i = 0; i < law.Values.Length; i++)
        {
            if (law.Values[i].Kind != ExprType.Boolean)
            {
                throw new StructuredDeclinedException("a condition can fail on a reachable outcome");
            }

            if (law.Values[i].BoolValue)
            {
                mass += law.Mass[i];
            }
        }

        return new Rational(mass, law.Total);
    }

    private static string Key(List<Term> guard) => string.Join(',', guard.Select(t => t.Id));

    /// <summary>Add a condition to a conjunction kept as sorted distinct terms. Returns null when it is plainly false.</summary>
    private List<Term>? With(List<Term> guard, Term condition, bool negate)
    {
        var result = new List<Term>(guard);
        return Flatten(condition, negate, result) ? result.DistinctBy(t => t.Id).OrderBy(t => t.Id).ToList() : null;
    }

    private bool Flatten(Term condition, bool negate, List<Term> into)
    {
        switch (condition)
        {
            case ConstTerm constant:
                return constant.Value.Kind == ExprType.Boolean && constant.Value.BoolValue != negate;
            case LogicTerm logic
                when logic.IsOr == negate:
                return Flatten(logic.A, negate, into) && Flatten(logic.B, negate, into);
            case OpTerm { Code: Op.Not } not when not.Args[0].IsBool: return Flatten(not.Args[0], !negate, into);
            default:
                Term literal = negate ? f.Not(condition) : condition;
                if (literal is ConstTerm)
                {
                    return Flatten(literal, false, into);
                }

                into.Add(literal);
                return true;
        }
    }

    private Term Conjunction(IReadOnlyList<Term> terms)
    {
        Term all = f.TrueT;
        foreach (Term term in terms)
        {
            all = f.And(all, term);
        }

        return all;
    }

    /// <summary>P(all conditions hold): the product over independent groups of conditions.</summary>
    private Rational GuardProbability(List<Term> guard)
    {
        if (guard.Count == 0)
        {
            return Rational.One;
        }

        var key = Key(guard);
        if (_guardProbability.TryGetValue(key, out Rational known))
        {
            return known;
        }

        Rational probability = Rational.One;
        foreach (List<int> group in Components(guard))
        {
            probability *= Probability(Conjunction(group.Select(i => guard[i]).ToList()));
            if (probability.IsZero)
            {
                break;
            }
        }

        return _guardProbability[key] = probability;
    }

    /// <summary>E[term · 1{guard}].</summary>
    private Rational Expect(List<Term> guard, string guardKey, Term term)
    {
        if (term is ConstTerm constant)
        {
            // A failure or a non-amount is harmless only where no outcome selects it.
            if (constant.IsErr || constant.Value.Kind != ExprType.Number)
            {
                return GuardProbability(guard).IsZero
                    ? Rational.Zero
                    : throw new StructuredDeclinedException("an amount is not numeric on a reachable outcome");
            }

            return V.IsZero(constant.Value)
                ? Rational.Zero
                : new Rational(constant.Value.NumberNumerator, constant.Value.NumberDenominator) *
                  GuardProbability(guard);
        }

        if (_expectations.TryGetValue((guardKey, term.Id), out Rational known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        Stats.Expectations++;
        f.Budget.Tick(guard.Count + 1);

        Rational result;
        switch (term)
        {
            case LinTerm lin:
                result = V.IsZero(lin.C0)
                    ? Rational.Zero
                    : new Rational(lin.C0.NumberNumerator, lin.C0.NumberDenominator) * GuardProbability(guard);
                for (var i = 0; i < lin.Terms.Length; i++)
                {
                    result += new Rational(lin.Coefs[i].NumberNumerator, lin.Coefs[i].NumberDenominator) *
                              Expect(guard, guardKey, lin.Terms[i]);
                }

                break;
            // Given the loop's entry and exit the amount is independent of every condition, so its conditional mean stands for it.
            case MomentTerm moment:
                result = Expect(guard, guardKey, moment.Mean);
                break;
            case OpTerm { Code: Op.Arith, Aux: (int)BinaryOp.Mul } product
                when Bits.Intersects(product.Support, f.MomentSlots):
                result = Product(guard, guardKey, product.Args[0], product.Args[1]);
                break;
            // Total expectation over a condition, taken only where a branch is itself a sum, a conditional or a constant.
            case IfTerm { C.IsBool: true } conditional when Splits(conditional):
                result = Rational.Zero;
                if (conditional.A is not ConstTerm { IsErr: false } a || !V.IsZero(a.Value))
                {
                    if (With(guard, conditional.C, false) is { } then)
                    {
                        result += Expect(then, Key(then), conditional.A);
                    }
                }

                if (conditional.B is not ConstTerm { IsErr: false } b || !V.IsZero(b.Value))
                {
                    if (With(guard, conditional.C, true) is { } otherwise)
                    {
                        result += Expect(otherwise, Key(otherwise), conditional.B);
                    }
                }

                break;
            default:
                result = Leaf(guard, term);
                break;
        }

        return _expectations[(guardKey, term.Id)] = result;
    }

    private static bool Splits(IfTerm conditional) =>
        conditional.A is ConstTerm or LinTerm or IfTerm or MomentTerm ||
        conditional.B is ConstTerm or LinTerm or IfTerm or MomentTerm;

    /// <summary>E[term²]: with <see cref="Expect(Term)" /> it gives the variance without the law of the term.</summary>
    public Rational SecondMoment(Term term) => Product([], "", term, term);

    private Rational Scaled(List<Term> guard, ConstTerm constant, Rational expectation)
    {
        if (constant.IsErr || constant.Value.Kind != ExprType.Number)
        {
            return GuardProbability(guard).IsZero
                ? Rational.Zero
                : throw new StructuredDeclinedException("an amount is not numeric on a reachable outcome");
        }

        return new Rational(constant.Value.NumberNumerator, constant.Value.NumberDenominator) * expectation;
    }

    /// <summary>
    ///     E[x · y · 1{guard}], bilinear in x and y: sums are expanded, conditionals split the guard, and a pair of
    ///     parts that share no variable is the product of two expectations. Only a pair that does share variables
    ///     needs a law, and then only the law of that one product.
    /// </summary>
    private Rational Product(List<Term> guard, string guardKey, Term x, Term y)
    {
        if (x is ConstTerm cx)
        {
            return V.IsZero(cx.Value) && !cx.IsErr ? Rational.Zero : Scaled(guard, cx, Expect(guard, guardKey, y));
        }

        if (y is ConstTerm cy)
        {
            return V.IsZero(cy.Value) && !cy.IsErr ? Rational.Zero : Scaled(guard, cy, Expect(guard, guardKey, x));
        }

        if (x.Id > y.Id)
        {
            (x, y) = (y, x);
        }

        if (_products.TryGetValue((guardKey, x.Id, y.Id), out Rational known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        f.Budget.Tick(guard.Count + 1);
        Rational result = Rational.Zero;
        if ((x is LinTerm ? x : y is LinTerm ? y : null) is LinTerm lin)
        {
            Term other = ReferenceEquals(lin, x) ? y : x;
            if (!V.IsZero(lin.C0))
            {
                result = new Rational(lin.C0.NumberNumerator, lin.C0.NumberDenominator) *
                         Expect(guard, guardKey, other);
            }

            for (var i = 0; i < lin.Terms.Length; i++)
            {
                result += new Rational(lin.Coefs[i].NumberNumerator, lin.Coefs[i].NumberDenominator) *
                          Product(guard, guardKey, lin.Terms[i], other);
            }
        }
        else if ((x is IfTerm { C.IsBool: true } a && Splits(a) ? a :
                     y is IfTerm { C.IsBool: true } b && Splits(b) ? b : null) is { } conditional)
        {
            Term other = ReferenceEquals(conditional, x) ? y : x;
            if (With(guard, conditional.C, false) is { } then)
            {
                result += Product(then, Key(then), conditional.A, other);
            }

            if (With(guard, conditional.C, true) is { } otherwise)
            {
                result += Product(otherwise, Key(otherwise), conditional.B, other);
            }
        }
        else if ((x as MomentTerm ?? y as MomentTerm) is { } moment)
        {
            Term other = ReferenceEquals(moment, x) ? y : x;
            if (other is MomentTerm partner && partner.Loop == moment.Loop)
            {
                if (moment.Second.Length == 0)
                {
                    throw new StructuredLawUnavailableException(
                        "the second moment of an amount accumulated by a long loop was not computed within its work allowance");
                }

                result = Expect(guard, guardKey, moment.Second[partner.Index]);
            }
            else if (Bits.Has(other.Support, moment.Loop))
            {
                // The other factor holds an amount of the same loop inside an operator: that needs their joint law.
                if (other is OpTerm { Code: Op.Arith, Aux: (int)BinaryOp.Mul } nested)
                {
                    result = JointProduct(guard, [moment, .. nested.Args]);
                }
                else
                {
                    Lawless(other);
                }
            }
            else
            {
                result = Product(guard, guardKey, moment.Mean, other);
            }
        }
        else
        {
            result = Pair(guard, x, y);
        }

        return _products[(guardKey, x.Id, y.Id)] = result;
    }

    /// <summary>E[x · y · 1{guard}] for two parts that are neither sums nor conditionals.</summary>
    private Rational Pair(List<Term> guard, Term x, Term y)
    {
        // Conditions tie the factors together as much as shared variables do.
        var (supportX, supportY) = (x.Support, y.Support);
        var forX = new List<Term>();
        var forY = new List<Term>();
        var rest = new List<Term>(guard);
        var joint = ReferenceEquals(x, y) || Bits.Intersects(supportX, supportY);
        for (var changed = true; changed && !joint;)
        {
            changed = false;
            for (var i = rest.Count - 1; i >= 0 && !joint; i--)
            {
                var (touchesX, touchesY) = (Bits.Intersects(rest[i].Support, supportX),
                    Bits.Intersects(rest[i].Support, supportY));
                if (!touchesX && !touchesY)
                {
                    continue;
                }

                joint = touchesX && touchesY;
                if (touchesX)
                {
                    supportX = Bits.Union(supportX, rest[i].Support);
                    forX.Add(rest[i]);
                }
                else
                {
                    supportY = Bits.Union(supportY, rest[i].Support);
                    forY.Add(rest[i]);
                }

                rest.RemoveAt(i);
                changed = true;
            }
        }

        if (joint && f.Fault != StructuredFault.ProductsTakenIndependent)
        {
            return Leaf(guard, f.Arith(BinaryOp.Mul, x, y));
        }

        if (f.Fault == StructuredFault.ProductsTakenIndependent)
        {
            return GuardProbability(guard).IsZero
                ? Rational.Zero
                : Expect(guard, Key(guard), x) * Expect(guard, Key(guard), y) / GuardProbability(guard);
        }

        Rational outside = GuardProbability(rest);
        if (outside.IsZero)
        {
            return Rational.Zero;
        }

        forX.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        forY.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return outside * Expect(forX, Key(forX), x) * Expect(forY, Key(forY), y);
    }

    private Rational Leaf(List<Term> guard, Term term)
    {
        // Conditions that share no variable with the term, directly or through other conditions, factor out.
        var support = term.Support;
        var dependent = new List<Term>();
        var rest = new List<Term>(guard);
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = rest.Count - 1; i >= 0; i--)
            {
                if (!Bits.Intersects(rest[i].Support, support))
                {
                    continue;
                }

                support = Bits.Union(support, rest[i].Support);
                dependent.Add(rest[i]);
                rest.RemoveAt(i);
                changed = true;
            }
        }

        Rational outside = GuardProbability(rest);
        if (outside.IsZero)
        {
            return Rational.Zero;
        }

        if (dependent.Count == 0)
        {
            return outside * Mean(LawOf(term));
        }

        dependent.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return outside * Mean(LawOf(f.If(Conjunction(dependent), term, f.ZeroT)));
    }

    private bool NonNegative(Term term) =>
        BoundOf(term) is { } bound && ExprValue.CompareNumbers(bound.Lo, V.Zero) >= 0;

    /// <summary>
    ///     A Boolean term for "term &gt; 0", written over the parts of the amount: a sum of non-negative parts is
    ///     positive when one part is, a product when both factors are. P(win &gt; 0) then needs the joint law of a
    ///     few indicators instead of the law of the amount.
    ///     It equals the comparison on every outcome where the amount does not fail, so it is used only once
    ///     <see cref="Expect(Term)" /> has shown that no reachable outcome makes the amount fail.
    /// </summary>
    public Term Positive(Term term)
    {
        if (term is ConstTerm { IsErr: false, Value.Kind: ExprType.Number } constant)
        {
            return f.Bool(ExprValue.CompareNumbers(constant.Value, V.Zero) > 0);
        }

        if (_positive.TryGetValue(term.Id, out Term? known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        f.Budget.Tick();
        Term? result = null;
        switch (term)
        {
            // One amount per loop: two of them are dependent in a way their separate indicators do not carry.
            case MomentTerm { Positive: not null } moment
                when !_positiveOf.TryGetValue(moment.Loop, out var index) || index == moment.Index:
                _positiveOf[moment.Loop] = moment.Index;
                result = moment.Positive();
                break;
            case LinTerm lin when ExprValue.CompareNumbers(lin.C0, V.Zero) >= 0 &&
                                  lin.Coefs.All(c => ExprValue.CompareNumbers(c, V.Zero) > 0) &&
                                  lin.Terms.All(NonNegative):
                result = f.Bool(!V.IsZero(lin.C0));
                foreach (Term part in lin.Terms)
                {
                    result = f.Or(result, Positive(part));
                    if (f.Fault == StructuredFault.HitFromFirstPart)
                    {
                        break;
                    }
                }

                break;
            case IfTerm { C.IsBool: true } conditional:
                result = f.If(conditional.C, Positive(conditional.A), Positive(conditional.B));
                break;
            case OpTerm { Code: Op.Arith, Aux: (int)BinaryOp.Mul } product
                when product.Args.All(a => a.IsNum && NonNegative(a)):
                result = f.And(Positive(product.Args[0]), Positive(product.Args[1]));
                break;
            case OpTerm { Code: Op.Call, Payload: "max", Args.Length: 2 } extreme
                when extreme.Args.All(a => a.IsNum && NonNegative(a)):
                result = f.Or(Positive(extreme.Args[0]), Positive(extreme.Args[1]));
                break;
        }

        return _positive[term.Id] = result ?? f.Cmp(CompareOp.Gt, term, f.ZeroT);
    }

    private static ExprValue Min(ExprValue a, ExprValue b) => ExprValue.CompareNumbers(a, b) <= 0 ? a : b;
    private static ExprValue Max(ExprValue a, ExprValue b) => ExprValue.CompareNumbers(a, b) >= 0 ? a : b;
    private static Bound Hull(Bound a, Bound b) => new(Min(a.Lo, b.Lo), Max(a.Hi, b.Hi), Common(a.Step, b.Step));

    /// <summary>The step of a value, as a positive number: the value is a whole multiple of it, and of nothing larger.</summary>
    private static Rational StepOf(ExprValue value) =>
        new(BigInteger.Abs(value.NumberNumerator), value.NumberDenominator);

    /// <summary>
    ///     The largest step that whole multiples of <paramref name="a" /> and of <paramref name="b" /> are both whole
    ///     multiples of: their greatest common divisor as rationals. Zero is the step of the value zero alone.
    /// </summary>
    internal static Rational Common(Rational a, Rational b) => a.IsZero ? b :
        b.IsZero ? a :
        new Rational(BigInteger.GreatestCommonDivisor(a.Numerator * b.Denominator, b.Numerator * a.Denominator),
            a.Denominator * b.Denominator);

    private static Bound? OfValues(IEnumerable<ExprValue> values)
    {
        Bound? bound = null;
        foreach (ExprValue value in values)
        {
            if (V.IsErr(value))
            {
                continue;
            }

            if (value.Kind != ExprType.Number)
            {
                return null;
            }

            var point = new Bound(value, value, StepOf(value));
            bound = bound is { } so ? Hull(so, point) : point;
        }

        return bound;
    }

    public Bound? BoundOf(Term term)
    {
        if (_bounds.TryGetValue(term.Id, out Bound? known))
        {
            return known;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        Bound? bound = null;
        if (term.Vals is { Length: > 0 } values)
        {
            bound = OfValues(values);
        }
        else
        {
            switch (term)
            {
                case MomentTerm { Bounded: true } moment: bound = new Bound(moment.Lo, moment.Hi, moment.Step); break;
                case TableTerm table: bound = OfValues(table.Values); break;
                case LinTerm lin:
                {
                    var sum = new Bound(lin.C0, lin.C0, StepOf(lin.C0));
                    for (var i = 0; i < lin.Terms.Length && sum is { } so; i++)
                    {
                        if (BoundOf(lin.Terms[i]) is not { } part)
                        {
                            bound = null;
                            goto done;
                        }

                        ExprValue coef = lin.Coefs[i];
                        (ExprValue x, ExprValue y) = (ExprValue.Mul(coef, part.Lo), ExprValue.Mul(coef, part.Hi));
                        sum = new Bound(ExprValue.Add(so.Lo, Min(x, y)), ExprValue.Add(so.Hi, Max(x, y)),
                            Common(so.Step, StepOf(coef) * part.Step));
                        bound = sum;
                    }

                    bound = sum;
                    break;
                }
                case IfTerm conditional:
                    bound = BoundOf(conditional.A) is { } a && BoundOf(conditional.B) is { } b ? Hull(a, b) : null;
                    break;
                case SelTerm select:
                    foreach (Term child in select.Children)
                    {
                        if (BoundOf(child) is not { } part)
                        {
                            bound = null;
                            break;
                        }

                        bound = bound is { } so ? Hull(so, part) : part;
                    }

                    break;
                case OpTerm { Code: Op.Extract } extract:
                    bound = BoundOf(extract.Args[0]) is { } inner && extract.Args[0].IsNum
                        ? new Bound(Min(inner.Lo, V.Zero), Max(inner.Hi, V.Zero), Rational.One)
                        : null;
                    break;
                case OpTerm { Code: Op.Arith } arithmetic when BoundOf(arithmetic.Args[0]) is { } l &&
                                                               BoundOf(arithmetic.Args[1]) is { } r
                                                               && arithmetic.Args[0].IsNum && arithmetic.Args[1].IsNum:
                    switch ((BinaryOp)arithmetic.Aux)
                    {
                        case BinaryOp.Add:
                            bound = new Bound(ExprValue.Add(l.Lo, r.Lo), ExprValue.Add(l.Hi, r.Hi),
                                Common(l.Step, r.Step)); break;
                        case BinaryOp.Sub:
                            bound = new Bound(ExprValue.Sub(l.Lo, r.Hi), ExprValue.Sub(l.Hi, r.Lo),
                                Common(l.Step, r.Step)); break;
                        case BinaryOp.Mul:
                            ExprValue[] corners =
                            [
                                ExprValue.Mul(l.Lo, r.Lo), ExprValue.Mul(l.Lo, r.Hi), ExprValue.Mul(l.Hi, r.Lo),
                                ExprValue.Mul(l.Hi, r.Hi)
                            ];
                            bound = new Bound(corners.Aggregate(Min), corners.Aggregate(Max), l.Step * r.Step);
                            break;
                    }

                    break;
                case OpTerm { Code: Op.Call, Payload: "min" or "max", Args.Length: 2 } extreme
                    when BoundOf(extreme.Args[0]) is { } l && BoundOf(extreme.Args[1]) is { } r &&
                         extreme.Args[0].IsNum && extreme.Args[1].IsNum:
                    bound = (string)extreme.Payload == "min"
                        ? new Bound(Min(l.Lo, r.Lo), Min(l.Hi, r.Hi), Common(l.Step, r.Step))
                        : new Bound(Max(l.Lo, r.Lo), Max(l.Hi, r.Hi), Common(l.Step, r.Step));
                    break;
            }
        }

        done:
        return _bounds[term.Id] = bound;
    }

    /// <summary>
    ///     Masses collected by value. Every value comes with its hash, which laws keep, so a value is hashed once
    ///     however many mixtures it passes through.
    /// </summary>
    private sealed class Masses
    {
        public int Count;
        public int[] Keys = [];
        public BigInteger[] Mass = [];
        public ExprValue[] Values = [];
        private int _mask;
        private int[] _slots = [];

        public void Reset(int capacity)
        {
            Array.Clear(Values, 0, Count);
            Count = 0;
            var slots = (int)BitOperations.RoundUpToPowerOf2((uint)System.Math.Max(capacity, 8) * 2);
            if (_slots.Length == slots)
            {
                Array.Clear(_slots);
            }
            else
            {
                _slots = new int[slots];
            }

            _mask = slots - 1;
        }

        public void Add(in ExprValue value, int key, BigInteger mass)
        {
            if (mass.IsZero)
            {
                return;
            }

            var slot = Spread(key) & _mask;
            for (int entry; (entry = _slots[slot]) != 0; slot = (slot + 1) & _mask)
            {
                if (Keys[entry - 1] != key || !Values[entry - 1].Equals(value))
                {
                    continue;
                }

                Mass[entry - 1] += mass;
                return;
            }

            if (Count == Keys.Length)
            {
                Grow();
            }

            Keys[Count] = key;
            Values[Count] = value;
            Mass[Count] = mass;
            _slots[slot] = ++Count;
            if (Count * 2 > _slots.Length)
            {
                Rehash();
            }
        }

        private static int Spread(int key) => (int)(((uint)key * 0x9E3779B1u) >> 7);

        private void Grow()
        {
            var size = System.Math.Max(16, Keys.Length * 2);
            Array.Resize(ref Keys, size);
            Array.Resize(ref Values, size);
            Array.Resize(ref Mass, size);
        }

        private void Rehash()
        {
            _slots = new int[_slots.Length * 2];
            _mask = _slots.Length - 1;
            for (var entry = 0; entry < Count; entry++)
            {
                var slot = Spread(Keys[entry]) & _mask;
                while (_slots[slot] != 0)
                {
                    slot = (slot + 1) & _mask;
                }

                _slots[slot] = entry + 1;
            }
        }
    }

    // ── Substitution ─────────────────────────────────────────────────────

    /// <summary>
    ///     What to substitute: a variable's outcome, or whole sub-terms. <see cref="Affected" /> bounds the variables
    ///     involved, so sub-terms that cannot change are returned as they are.
    /// </summary>
    private readonly record struct Context(
        Rv? Variable,
        int Outcome,
        Dictionary<Term, Term>? Replace,
        ulong[] Affected,
        Dictionary<Term, Term> Memo);

    // ── Bounds ───────────────────────────────────────────────────────────

    /// <summary>
    ///     For the numeric values of a term: a range that contains them and a step they are all whole multiples of
    ///     (zero when they are all zero). Payouts of 5, 20 and 135 credits have a step of 5, so a thirtieth of
    ///     their sum is known to be a whole number of sixths.
    /// </summary>
    internal readonly record struct Bound(ExprValue Lo, ExprValue Hi, Rational Step);
}
