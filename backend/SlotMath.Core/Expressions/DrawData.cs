using SlotMath.Core.Model;

namespace SlotMath.Core.Expressions;

/// <summary>Generic categorical data contracts shared by all execution paths.</summary>
internal static class DrawData
{
    internal static IEnumerable<DrawWeight[]> Rows(DrawNode draw) =>
        draw.BoundDistributions ?? (draw.DrawWeights is { } outcomes ? [outcomes] : []);

    internal static ExprValue StateValue(DrawWeight outcome) => outcome.StateValue is { } literal
        ? ExactExpressionEvaluator.Evaluate(literal, EvalContext.Empty)
        : ExprValue.String(outcome.OutcomeId);

    internal static void Validate(IEnumerable<DrawWeight[]> rows)
    {
        ExprType? kind = null;
        foreach (DrawWeight outcome in rows.SelectMany(row => row))
        {
            if (outcome is null || outcome.OutcomeId is null || outcome.StateValue is { Value: null })
            {
                throw new ArgumentException("Categorical outcomes and state literals must be defined.");
            }

            if (outcome.StateValue is { Value.Length: > 4096 })
            {
                throw new ArgumentException("A categorical state literal exceeds 4096 characters.");
            }

            ExprType next = StateValue(outcome).Kind;
            if (kind is { } previous && previous != next)
            {
                throw new ArgumentException(
                    "Categorical state outcomes require one common scalar type across all family members.");
            }

            kind = next;
        }
    }

    internal static ExprType StateType(DrawNode draw) =>
        Rows(draw).SelectMany(row => row).Select(outcome => StateValue(outcome).Kind).FirstOrDefault(ExprType.String);

    internal static int FamilyIndex(ExprValue value, int length)
    {
        const string location = "distribution.selector";
        if (value.Kind != ExprType.Number)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "A distribution selector must be a Number.",
                location);
        }

        if (!value.NumberDenominator.IsOne)
        {
            throw new ExpressionEvaluationException("EVAL_INVALID_INDEX", "A distribution selector must be an integer.",
                location);
        }

        if (value.NumberNumerator < 0 || value.NumberNumerator >= length)
        {
            throw StateValues.IndexError(value.NumberNumerator, length, location);
        }

        return (int)value.NumberNumerator;
    }
}
