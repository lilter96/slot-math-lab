using System.Numerics;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Expressions;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionCompiler — compiles expression ASTs to typed delegates
//
//  Compiled delegates are pure functions of the game state only (invariant 4:
//  there is no Board type — a board is a user-defined array in state S, read via
//  state["board"] + fold/map/filter/aggregate):
//
//   - Number port:   Func<object?, BigInteger> (exact)
//                    Func<object?, double>     (sampled)
//   - Boolean port:  Func<object?, bool>
//   - Weight port:   Func<object?, WeightSet>
//   - String port:   Func<object?, string?>
//
//  This lets the graph compiler (G14) treat expression-valued ports and
//  library/plugin nodes uniformly — both are functions of the state.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Typed delegate factories for compiled expressions.
/// </summary>
public static class ExpressionCompiler
{
    // ── Exact path delegates (BigInteger rational amounts) ───────────────

    /// <summary>
    /// Compile a Number-typed expression into a delegate returning an exact
    /// BigInteger value (truncated rational to integer).
    /// </summary>
    public static Func<object?, BigInteger> CompileNumber(Expression expr) =>
        state => ExactExpressionEvaluator.EvaluateAsInteger(expr, new EvalContext { State = state });

    /// <summary>
    /// Compile a Number-typed expression into a delegate returning a double
    /// (for hybrid / sampled paths).
    /// </summary>
    public static Func<object?, double> CompileNumberDouble(Expression expr) =>
        state => SampledExpressionEvaluator.Evaluate(expr, new EvalContext { State = state });

    /// <summary>
    /// Compile a Boolean-typed expression into a predicate delegate (trigger /
    /// stop / condition ports).
    /// </summary>
    public static Func<object?, bool> CompileBoolean(Expression expr) =>
        state => ExactExpressionEvaluator.EvaluateAsBool(expr, new EvalContext { State = state });

    /// <summary>
    /// Compile a String-typed expression into a delegate.
    /// </summary>
    public static Func<object?, string?> CompileString(Expression expr) =>
        state =>
        {
            var v = ExactExpressionEvaluator.Evaluate(expr, new EvalContext { State = state });
            return v.Kind == ExprType.String ? v.StringValue : v.ToString();
        };

    // ── Weight compilation (for Draw nodes) ──────────────────────────────

    /// <summary>
    /// Compile a weight expression into a <c>Func&lt;S, WeightSet&gt;</c> for
    /// state-dependent Draw weights.
    /// </summary>
    public static Func<object?, WeightSet> CompileWeights(Expression expr) =>
        state => ExactExpressionEvaluator.EvaluateAsWeights(expr, new EvalContext { State = state });

    /// <summary>
    /// Compile a generic expression to a <c>Func&lt;object?, object&gt;</c>
    /// based on the target type.
    /// </summary>
    public static Func<object?, object> CompileBoxed(Expression expr, ExprType targetType) =>
        targetType switch
        {
            ExprType.Number => state =>
                ExactExpressionEvaluator.EvaluateAsInteger(expr, new EvalContext { State = state }),
            ExprType.Boolean => state =>
                ExactExpressionEvaluator.EvaluateAsBool(expr, new EvalContext { State = state }),
            ExprType.String or ExprType.Symbol => CompileStringBoxed(expr),
            ExprType.Weights => state =>
                ExactExpressionEvaluator.EvaluateAsWeights(expr, new EvalContext { State = state }),
            _ => _ => 0,
        };

    private static Func<object?, object> CompileStringBoxed(Expression expr) => state =>
    {
        var v = ExactExpressionEvaluator.Evaluate(expr, new EvalContext { State = state });
        return v.Kind == ExprType.String ? v.StringValue! : v.ToString();
    };

    /// <summary>
    /// Compile to a win-producing delegate of the same shape a fast-path
    /// evaluator uses — a function of the state returning Win[].  Proof that
    /// expression-compiled functions and evaluators share one shape.
    /// </summary>
    public static Func<object?, Win[]> CompileAsEvaluator(Expression expr, string symbolId) =>
        state =>
        {
            var amount = ExactExpressionEvaluator.EvaluateAsInteger(expr, new EvalContext { State = state });
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
