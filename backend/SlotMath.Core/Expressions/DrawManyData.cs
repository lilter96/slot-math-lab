namespace SlotMath.Core.Expressions;

/// <summary>Resource and exact-integer count contract for ordered categorical batches.</summary>
internal static class DrawManyData
{
    internal const int MaxCount = 4096;

    internal static int Count(ExprValue value, int maximum)
    {
        const string location = "drawMany.count";
        if (value.Kind != ExprType.Number)
        {
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", "A draw count must be a Number.", location);
        }

        if (!value.NumberDenominator.IsOne)
        {
            throw new ExpressionEvaluationException("EVAL_INVALID_INDEX", "A draw count must be an integer.", location);
        }

        if (value.NumberNumerator < 0 || value.NumberNumerator > maximum)
        {
            throw new ExpressionEvaluationException("EVAL_BATCH_RANGE", $"A draw count must be in [0,{maximum}].",
                location);
        }

        return (int)value.NumberNumerator;
    }
}
