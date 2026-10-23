using SlotMath.Core.Expressions;
using SlotMath.Core.Random;

namespace SlotMath.Core.Compiler;

/// <summary>One atomic array commit around an explicitly ordered sequence of scalar aliases.</summary>
internal sealed class SamplingDrawMany(
    SamplingDraw[] rows,
    bool repeat,
    int destination,
    int? fixedCount,
    int maximum,
    Func<SamplingFrame, ExprValue>? count)
{
    internal object? Run(SamplingFrame frame, SeededRandom random, object? input)
    {
        var length = fixedCount ?? DrawManyData.Count(count!(frame), maximum);
        ExprValue[] values = frame.NewArray(length);
        var last = input;
        for (var index = 0; index < length; index++)
        {
            if ((index & 63) == 0)
            {
                frame.CancellationToken.ThrowIfCancellationRequested();
            }

            last = rows[repeat ? 0 : index].Pick(random, out values[index]);
        }

        frame.CancellationToken.ThrowIfCancellationRequested();
        frame.Cells[destination].SetTyped(ExprValue.Array(values));
        return last;
    }
}
