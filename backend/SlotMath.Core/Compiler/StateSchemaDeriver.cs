using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  StateSchemaDeriver — Graph Truth → Everything Derived
//
//  The GraphConfig is the single authored truth.  The shape of the recurrence
//  state S is NOT hand-declared — it is DERIVED from what the graph actually
//  writes:
//    • a reel-strip Draw publishes the board array + its dimensions
//      (state["board"], state["rows"], state["cols"]);
//    • a DataNode injects a named array;
//    • a ModifyState node writes its output key, whose TYPE is the type of the
//      expression that produces it (fold/map/filter → Array, sums → Number, …);
//    • a Loop publishes its iteration counter and win accumulator;
//    • a PutState / Draw state-write key.
//
//  Author-declared StateSchema entries (if any) win — they are explicit
//  overrides — but are entirely OPTIONAL.  The derived schema feeds the
//  expression type-checker, so a graph that uses the catalog mechanics needs
//  no hand-written state schema and cannot drift from what it writes.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Derives the complete, typed recurrence-state schema from a (post-inline)
/// GraphConfig — the single source of truth.
/// </summary>
public static class StateSchemaDeriver
{
    /// <summary>
    /// Derive the typed state fields written by the graph.  Each field's type is
    /// inferred from the expression that writes it (fixpoint over ModifyState
    /// outputs); author StateSchema entries override.
    /// </summary>
    public static IReadOnlyList<FieldDescriptor> Derive(GraphConfig config)
    {
        var fields = new Dictionary<string, ExprType>(StringComparer.Ordinal);
        var authored = new HashSet<string>(config.StateSchema.Select(s => s.Name), StringComparer.Ordinal);

        // (1) Structural truth from node kinds.
        foreach (var node in config.Nodes)
        {
            switch (node)
            {
                case DrawNode d when IsReelDraw(d, config):
                    // A reel-strip draw publishes board + dims (GraphCompiler).
                    fields[d.BoardStateKey ?? GridState.CellsKey] = ExprType.Array;
                    fields[GridState.RowsKey] = ExprType.Number;
                    fields[GridState.ColsKey] = ExprType.Number;
                    if (d.StateWriteKey != null) fields[d.StateWriteKey] = ExprType.String;
                    break;
                case DrawNode d:
                    // A weighted value draw may publish a board key / outcome id.
                    if (d.BoardStateKey != null) fields[d.BoardStateKey] = ExprType.Array;
                    if (d.StateWriteKey != null) fields[d.StateWriteKey] = ExprType.String;
                    break;
                case DataNode dn:
                    fields[dn.StateKey] = ExprType.Array;
                    break;
                case PutStateNode p:
                    if (!fields.ContainsKey(p.StateKey)) fields[p.StateKey] = ExprType.Number;
                    break;
                case LoopNode l:
                    fields[$"__iter_{l.Id}__"] = ExprType.Number;
                    fields[$"__wins_{l.Id}__"] = ExprType.Number;
                    break;
            }
        }

        // (2) Provisional seed for ModifyState output keys (shape of the writer).
        foreach (var m in config.Nodes.OfType<ModifyStateNode>())
        {
            if (m.OutputKey == null || authored.Contains(m.OutputKey)) continue;
            if (!fields.ContainsKey(m.OutputKey))
                fields[m.OutputKey] = ProvisionalType(LookupExpr(config, m.ExpressionId));
        }

        // (3) Author StateSchema overrides (explicit, optional).
        foreach (var sf in config.StateSchema)
            fields[sf.Name] = MapType(sf.Type);

        // (4) Fixpoint: a ModifyState output's type IS the type of its expression.
        for (var pass = 0; pass < 4; pass++)
        {
            var ctx = BuildContext(config, fields);
            var changed = false;
            foreach (var m in config.Nodes.OfType<ModifyStateNode>())
            {
                if (m.OutputKey == null || authored.Contains(m.OutputKey)) continue;
                var expr = LookupExpr(config, m.ExpressionId);
                if (expr == null) continue;
                var t = ExpressionTypeChecker.InferType(expr, ctx);
                if (t != ExprType.Error && fields.GetValueOrDefault(m.OutputKey) != t)
                {
                    fields[m.OutputKey] = t;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return fields.Select(kv => new FieldDescriptor { Name = kv.Key, Type = kv.Value })
                     .OrderBy(f => f.Name, StringComparer.Ordinal)
                     .ToList();
    }

    private static bool IsReelDraw(DrawNode d, GraphConfig config) =>
        // A reel-strip draw (no inline weight expression, reel sets present)
        // publishes the board array + its dimensions into state.
        d.WeightExpressionId == null && config.ReelSets.Length > 0;

    private static Expression? LookupExpr(GraphConfig config, string? id) =>
        id != null && config.Expressions != null && config.Expressions.TryGetValue(id, out var e) ? e : null;

    /// <summary>Shape-based seed so array-accumulating folds don't converge to Number.</summary>
    private static ExprType ProvisionalType(Expression? expr) => expr switch
    {
        MapExpr or FilterExpr => ExprType.Array,
        FoldExpr f => ContainsAppend(f.Body) ? ExprType.Array : ExprType.Number,
        CallExpr c when c.Function.Equals("append", StringComparison.OrdinalIgnoreCase) => ExprType.Array,
        _ => ExprType.Number,
    };

    private static bool ContainsAppend(Expression e) => e switch
    {
        CallExpr c when c.Function.Equals("append", StringComparison.OrdinalIgnoreCase) => true,
        IfExpr i => ContainsAppend(i.Condition) || ContainsAppend(i.ThenExpr) || ContainsAppend(i.ElseExpr),
        BinaryExpr b => ContainsAppend(b.Left) || ContainsAppend(b.Right),
        NotExpr n => ContainsAppend(n.Expr),
        CallExpr c => c.Args.Any(ContainsAppend),
        _ => false,
    };

    private static ExprType MapType(string? type) => type switch
    {
        "string" => ExprType.String,
        "boolean" => ExprType.Boolean,
        "array" => ExprType.Array,
        _ => ExprType.Number,
    };

    private static TypeCheckContext BuildContext(GraphConfig config, Dictionary<string, ExprType> fields)
    {
        var stateFields = fields.Select(kv => new FieldDescriptor { Name = kv.Key, Type = kv.Value }).ToList();
        // Synthetic "expressions" accessor (named expression references).
        if (config.Expressions is { Count: > 0 })
            stateFields.Add(new FieldDescriptor { Name = "expressions", Type = ExprType.Number });
        return new TypeCheckContext
        {
            ExpectedType = ExprType.Number,
            BoardFields = TypeCheckContext.Default.BoardFields,
            StateFields = stateFields,
            CellFields = TypeCheckContext.Default.CellFields,
            DecorationTypes = TypeCheckContext.Default.DecorationTypes,
        };
    }
}
