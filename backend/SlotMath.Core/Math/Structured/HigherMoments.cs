using System.Runtime.CompilerServices;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

internal sealed partial class Inference
{
    private readonly Dictionary<string, Rational> _jointProducts = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Term, int Order), Rational> _rawMoments = new();

    internal static int Binomial(int n, int k)
    {
        var value = 1;
        for (var i = 1; i <= k; i++)
        {
            value = value * (n - i + 1) / i;
        }

        return value;
    }

    public Rational RawMoment(Term term, int order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(order, 4);
        if (order == 0)
        {
            return Rational.One;
        }

        if (order == 1)
        {
            return Expect(term);
        }

        if (order == 2)
        {
            return SecondMoment(term);
        }

        if (_rawMoments.TryGetValue((term.Id, order), out Rational known))
        {
            return known;
        }

        f.Budget.Tick();
        Rational result;
        // Independent sums admit convolution of their moment sequences, without enumerating
        // multinomial combinations of all summands or constructing a payout distribution.
        if (IndependentMoment(term, order) is { } independent)
        {
            result = independent;
        }
        else
        {
            result = JointProduct([], Enumerable.Repeat(term, order).ToArray());
        }

        return _rawMoments[(term.Id, order)] = result;
    }

    private Rational? IndependentMoment(Term term, int order)
    {
        if (term is not LinTerm lin)
        {
            return null;
        }

        List<List<int>> groups = Components(lin.Terms);
        if (groups.Count < 2)
        {
            return null;
        }

        var moments = new Rational[order + 1];
        moments[0] = Rational.One;
        var constant = new Rational(lin.C0.NumberNumerator, lin.C0.NumberDenominator);
        for (var k = 1; k <= order; k++)
        {
            moments[k] = moments[k - 1] * constant;
        }

        foreach (List<int> group in groups)
        {
            Term part = f.Lin(V.Zero, group.Select(index => (lin.Coefs[index], lin.Terms[index])).ToList());
            var extra = new Rational[order + 1];
            for (var k = 0; k <= order; k++)
            {
                extra[k] = RawMoment(part, k);
            }

            var next = new Rational[order + 1];
            Array.Fill(next, Rational.Zero);
            for (var k = 0; k <= order; k++)
            {
                for (var j = 0; j <= k; j++)
                {
                    next[k] += Binomial(k, j) * moments[k - j] * extra[j];
                }
            }

            moments = next;
        }

        return moments[order];
    }

    public Rational ProductMany(Term? condition, Term[] terms)
    {
        if (condition is null)
        {
            return JointProduct([], terms);
        }

        return With([], condition, false) is { } guard ? JointProduct(guard, terms) : Rational.Zero;
    }

    private Rational JointProduct(List<Term> guard, Term[] input)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        f.Budget.Tick(guard.Count + input.Length + 1);
        if (input.Length == 0)
        {
            return GuardProbability(guard);
        }

        if (input.Length == 1)
        {
            return Expect(guard, Key(guard), input[0]);
        }

        if (input.Length == 2)
        {
            return Product(guard, Key(guard), input[0], input[1]);
        }

        if (input.Length > 256)
        {
            throw new StructuredLawUnavailableException("higher-moment product exceeds its factor budget");
        }

        Term[] terms = input.OrderBy(term => term.Id).ToArray();
        var key = Key(guard) + ":" + string.Join(',', terms.Select(term => term.Id));
        if (_jointProducts.TryGetValue(key, out Rational cached))
        {
            return cached;
        }

        Rational result;
        var constantIndex = Array.FindIndex(terms, term => term is ConstTerm);
        if (constantIndex >= 0)
        {
            var constant = (ConstTerm)terms[constantIndex];
            result = !constant.IsErr && V.IsZero(constant.Value)
                ? Rational.Zero
                : Scaled(guard, constant, JointProduct(guard, Remove(terms, constantIndex)));
        }
        else if (Array.FindIndex(terms, term => term is OpTerm { Code: Op.Arith, Aux: (int)BinaryOp.Mul }) is var
                     multiply && multiply >= 0)
        {
            var product = (OpTerm)terms[multiply];
            result = JointProduct(guard, [.. Remove(terms, multiply), .. product.Args]);
        }
        else
        {
            Term[] all = [.. terms, .. guard];
            List<List<int>> components = Components(all);
            if (components.Count > 1)
            {
                result = Rational.One;
                foreach (List<int> component in components)
                {
                    Term[] factors = component.Where(index => index < terms.Length).Select(index => all[index])
                        .ToArray();
                    List<Term> conditions = component.Where(index => index >= terms.Length).Select(index => all[index])
                        .ToList();
                    conditions.Sort(static (a, b) => a.Id.CompareTo(b.Id));
                    result *= JointProduct(conditions, factors);
                }
            }
            else if (guard.Count == 0 && terms.Length <= 4 && terms.All(term => term == terms[0]) &&
                     IndependentMoment(terms[0], terms.Length) is { } independent)
            {
                result = independent;
            }
            else if (Array.FindIndex(terms, term => term is LinTerm) is var sum && sum >= 0)
            {
                var lin = (LinTerm)terms[sum];
                Term[] rest = Remove(terms, sum);
                result = V.IsZero(lin.C0)
                    ? Rational.Zero
                    : new Rational(lin.C0.NumberNumerator, lin.C0.NumberDenominator) * JointProduct(guard, rest);
                for (var j = 0; j < lin.Terms.Length; j++)
                {
                    result += new Rational(lin.Coefs[j].NumberNumerator, lin.Coefs[j].NumberDenominator) *
                              JointProduct(guard, [.. rest, lin.Terms[j]]);
                }
            }
            else if (Array.FindIndex(terms, term => term is IfTerm { C.IsBool: true }) is var branch && branch >= 0)
            {
                var conditional = (IfTerm)terms[branch];
                Term[] rest = Remove(terms, branch);
                result = Rational.Zero;
                if (With(guard, conditional.C, false) is { } then)
                {
                    result += JointProduct(then, [.. rest, conditional.A]);
                }

                if (With(guard, conditional.C, true) is { } otherwise)
                {
                    result += JointProduct(otherwise, [.. rest, conditional.B]);
                }
            }
            else if (Array.FindIndex(terms, term => term is MomentTerm) is var at && at >= 0)
            {
                var moment = (MomentTerm)terms[at];
                MomentTerm[] same = terms.OfType<MomentTerm>().Where(part => part.Loop == moment.Loop).ToArray();
                Term[] rest = terms.Where(term => term is not MomentTerm part || part.Loop != moment.Loop).ToArray();
                foreach (Term other in rest)
                {
                    if (Bits.Has(other.Support, moment.Loop))
                    {
                        Lawless(other);
                    }
                }

                Term conditional = same.Length switch
                {
                    1 => moment.Mean,
                    2 when moment.Second.Length > 0 => moment.Second[same[1].Index],
                    _ when moment.Higher is not null => moment.Higher(same.Select(part => part.Index).ToArray()),
                    _ => throw new StructuredLawUnavailableException("the requested joint loop moment is unavailable")
                };
                result = JointProduct(guard, [.. rest, conditional]);
            }
            else
            {
                Term product = terms[0];
                for (var i = 1; i < terms.Length; i++)
                {
                    product = f.Arith(BinaryOp.Mul, product, terms[i]);
                }

                result = Leaf(guard, product);
            }
        }

        return _jointProducts[key] = result;
    }

    private static Term[] Remove(Term[] terms, int at) => [.. terms.Take(at), .. terms.Skip(at + 1)];
}
