using System.Globalization;
using System.Text;
using SlotMath.Core.Model;

namespace SlotMath.Codegen.Emit;

// ═══════════════════════════════════════════════════════════════════════════
//  ExpressionEmitter — level-(b) scalar expression AST → C# expression text.
//
//  Supported subset (G7 v1): constant (integer/boolean/string), state field
//  access, binary (+ − × and ∧ ∨), comparison, logical not, conditional.
//  Numbers are emitted as `long`; division and rationals are NOT supported
//  (they would break exact `long` semantics) and raise
//  CodegenUnsupportedException so the caller reports a diagnostic.  Array
//  iteration (fold/map/filter/aggregate) is out of scope here.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Translates a scalar <see cref="Expression"/> into a C# expression string,
/// resolving state-field references via the supplied resolver.
/// </summary>
public sealed class ExpressionEmitter(Func<string, string> resolveStateField)
{
    private readonly Func<string, string> _resolveStateField = resolveStateField;

    public string Emit(Expression expr) => expr switch
    {
        ConstantExpr c => EmitConstant(c),
        FieldAccessExpr f => EmitFieldAccess(f),
        BinaryExpr b => EmitBinary(b),
        CompareExpr c => EmitCompare(c),
        NotExpr n => $"(!{Emit(n.Expr)})",
        IfExpr i => $"({Emit(i.Condition)} ? {Emit(i.ThenExpr)} : {Emit(i.ElseExpr)})",
        _ => throw new CodegenUnsupportedException(
            $"expression node '{expr.GetType().Name}' is not supported by the G7 emitter (scalar subset only)."),
    };

    private static string EmitConstant(ConstantExpr c) => c.Kind switch
    {
        ConstantKind.Integer => $"{long.Parse(c.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}L",
        ConstantKind.Boolean => bool.Parse(c.Value) ? "true" : "false",
        ConstantKind.String => Quote(c.Value),
        ConstantKind.Rational => throw new CodegenUnsupportedException(
            "rational constants are not supported (exact long codegen only); use the interpreter."),
        _ => throw new CodegenUnsupportedException($"constant kind '{c.Kind}' is not supported."),
    };

    private string EmitFieldAccess(FieldAccessExpr f)
    {
        // Scalar subset: a single-segment reference to a state field.  Nested
        // paths (dict-cell metadata) and lambda-bound names (item/acc/index)
        // imply array iteration, which this emitter does not handle.
        if (f.Path is not { Length: 1 })
            throw new CodegenUnsupportedException(
                $"field access with path length {f.Path?.Length ?? 0} is not supported (scalar state fields only).");
        return _resolveStateField(f.Path[0]);
    }

    private string EmitBinary(BinaryExpr b) => b.Op switch
    {
        BinaryOp.Add => $"({Emit(b.Left)} + {Emit(b.Right)})",
        BinaryOp.Sub => $"({Emit(b.Left)} - {Emit(b.Right)})",
        BinaryOp.Mul => $"({Emit(b.Left)} * {Emit(b.Right)})",
        BinaryOp.And => $"({Emit(b.Left)} && {Emit(b.Right)})",
        BinaryOp.Or => $"({Emit(b.Left)} || {Emit(b.Right)})",
        BinaryOp.Div => throw new CodegenUnsupportedException(
            "division is not supported (exact long codegen only); use the interpreter."),
        _ => throw new CodegenUnsupportedException($"binary op '{b.Op}' is not supported."),
    };

    private string EmitCompare(CompareExpr c)
    {
        var op = c.Op switch
        {
            CompareOp.Eq => "==",
            CompareOp.Neq => "!=",
            CompareOp.Lt => "<",
            CompareOp.Gt => ">",
            CompareOp.Lte => "<=",
            CompareOp.Gte => ">=",
            _ => throw new CodegenUnsupportedException($"compare op '{c.Op}' is not supported."),
        };
        return $"({Emit(c.Left)} {op} {Emit(c.Right)})";
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString(),
            });
        sb.Append('"');
        return sb.ToString();
    }
}
