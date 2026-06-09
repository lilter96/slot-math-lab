using System.Numerics;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionCompiler — compiles expression ASTs to typed delegates
//
//  Compiled delegates match the same internal function types used by
//  the standard library and plugin implementations:
//
//   - Number port:   Func<Board?, object?, BigInteger> (exact)
//                    Func<Board?, object?, double>   (sampled)
//   - Boolean port:  Func<Board?, object?, bool>
//   - Weight port:   Func<object?, WeightSet>
//   - String port:   Func<Board?, object?, string>
//
//  This allows the graph compiler (G14) to treat expression-valued ports
//  and library/plugin nodes uniformly — both produce a function of the
//  same shape.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Typed delegate factories for compiled expressions.
/// </summary>
public static class ExpressionCompiler
{
    // ── Exact path delegates (BigInteger rational amounts) ───────────────

    /// <summary>
    /// Compile a Number-typed expression into a delegate that returns
    /// an exact BigInteger value (truncated rational to integer).
    ///
    /// This matches the internal signature used by library evaluators
    /// for multiplier/payout/adjustment ports.
    /// </summary>
    public static Func<Board?, object?, BigInteger> CompileNumber(
        Expression expr)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            return ExactExpressionEvaluator.EvaluateAsInteger(expr, ctx);
        };
    }

    /// <summary>
    /// Compile a Number-typed expression into a delegate that returns
    /// a double (for hybrid / sampled paths).
    /// </summary>
    public static Func<Board?, object?, double> CompileNumberDouble(
        Expression expr)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            return SampledExpressionEvaluator.Evaluate(expr, ctx);
        };
    }

    /// <summary>
    /// Compile a Boolean-typed expression into a predicate delegate.
    ///
    /// This matches the internal signature for trigger/condition ports.
    /// </summary>
    public static Func<Board?, object?, bool> CompileBoolean(
        Expression expr)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            return ExactExpressionEvaluator.EvaluateAsBool(expr, ctx);
        };
    }

    /// <summary>
    /// Compile a String-typed expression into a delegate.
    /// </summary>
    public static Func<Board?, object?, string?> CompileString(
        Expression expr)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            var v = ExactExpressionEvaluator.Evaluate(expr, ctx);
            return v.Kind == ExprType.String ? v.StringValue : v.ToString();
        };
    }

    // ── Weight compilation (for Draw nodes) ──────────────────────────────

    /// <summary>
    /// Compile a weight expression into a <c>Func&lt;S, WeightSet&gt;</c>.
    ///
    /// This matches the internal signature used by Draw nodes for
    /// state-dependent weights.  The expression is evaluated with
    /// the current state to produce a WeightSet.
    /// </summary>
    public static Func<object?, WeightSet> CompileWeights(
        Expression expr)
    {
        return (state) =>
        {
            var ctx = new EvalContext { State = state };
            return ExactExpressionEvaluator.EvaluateAsWeights(expr, ctx);
        };
    }

    /// <summary>
    /// Compile a generic expression to a <c>Func&lt;Board?, object?, T&gt;</c>
    /// based on the target type.
    /// </summary>
    public static Func<Board?, object?, object> CompileBoxed(
        Expression expr, ExprType targetType)
    {
        return targetType switch
        {
            ExprType.Number => (board, state) =>
            {
                var ctx = new EvalContext { Board = board, State = state };
                return ExactExpressionEvaluator.EvaluateAsInteger(expr, ctx);
            }
            ,
            ExprType.Boolean => (board, state) =>
            {
                var ctx = new EvalContext { Board = board, State = state };
                return ExactExpressionEvaluator.EvaluateAsBool(expr, ctx);
            }
            ,
            ExprType.String or ExprType.Symbol => (board, state) =>
            {
                var ctx = new EvalContext { Board = board, State = state };
                var v = ExactExpressionEvaluator.Evaluate(expr, ctx);
                return v.Kind == ExprType.String ? v.StringValue! : v.ToString();
            }
            ,
            ExprType.Weights => (board, state) =>
            {
                var ctx = new EvalContext { Board = board, State = state };
                return ExactExpressionEvaluator.EvaluateAsWeights(expr, ctx);
            }
            ,
            _ => (board, state) => 0,
        };
    }

    /// <summary>
    /// Compile to an <see cref="IEvaluator"/> delegate — the same interface
    /// the standard library evaluators implement.  The expression must
    /// resolve to a Win[] array (or a single BigInteger win).
    ///
    /// This is the proof that expression-compiled functions have the same
    /// shape as library/plugin evaluators.
    /// </summary>
    public static Func<Board, object?, Win[]> CompileAsEvaluator(
        Expression expr, string symbolId)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            var amount = ExactExpressionEvaluator.EvaluateAsInteger(expr, ctx);
            if (amount == 0)
                return Array.Empty<Win>();
            return new[] { new Win
            {
                SymbolId = symbolId,
                Count = 1,
                Positions = Array.Empty<(int Row, int Col)>(),
                Payout = (decimal)amount,
            } };
        };
    }

    /// <summary>
    /// Compile to an <see cref="ITransform"/>-compatible delegate.
    /// The expression evaluates to a board field value that the transform
    /// applies.  Returns (board, state) tuple.
    /// </summary>
    public static Func<Board, object?, (Board, object?)> CompileAsTransformOutput(
        Expression expr, Func<Board, BigInteger, Board> applyToBoard)
    {
        return (board, state) =>
        {
            var ctx = new EvalContext { Board = board, State = state };
            var value = ExactExpressionEvaluator.EvaluateAsInteger(expr, ctx);
            return (applyToBoard(board, value), state);
        };
    }
}
