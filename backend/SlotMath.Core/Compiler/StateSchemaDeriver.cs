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
        var fields = new Dictionary<string, FieldDescriptor>(StringComparer.Ordinal);
        foreach (var (key, value) in config.InitialState ?? new()) fields[key] = ExpressionShapes.FromJson(key, value);
        var authored = new HashSet<string>(config.StateSchema.Select(s => s.Name), StringComparer.Ordinal);
        void Write(string key, ExprType type) => fields[key] = ExpressionShapes.Field(key, type);
        void WriteArray(string key) => fields[key] = ExpressionShapes.Array(ExpressionShapes.Field("item", ExprType.String)) with { Name = key };
        if (config.Nodes.OfType<MetricsSinkNode>().Any(s => s.Settlement is not null))
            foreach (var key in MonetarySettlement.EvidenceKeys) Write(key, ExprType.Number);
        foreach (var node in config.Nodes)
        {
            switch (node)
            {
                case DrawNode d when IsReelDraw(d, config):
                    WriteArray(d.BoardStateKey ?? GridState.CellsKey);
                    Write(GridState.RowsKey, ExprType.Number); Write(GridState.ColsKey, ExprType.Number);
                    if (d.StateWriteKey != null) Write(d.StateWriteKey, ExprType.String);
                    break;
                case DrawNode d:
                    if (d.BoardStateKey != null) WriteArray(d.BoardStateKey);
                    if (d.StateWriteKey != null) Write(d.StateWriteKey, ExprType.String);
                    break;
                case DataNode dn: WriteArray(dn.StateKey); break;
                case PutStateNode p:
                    if (!fields.ContainsKey(p.StateKey)) Write(p.StateKey, ExprType.Number);
                    break;
                case LoopNode l: Write($"__exitReason_{l.Id}__", ExprType.String); Write($"__iter_{l.Id}__", ExprType.Number); Write($"__wins_{l.Id}__", ExprType.Number); break;
            }
        }
        foreach (var m in config.Nodes.OfType<ModifyStateNode>())
            if (m.OutputKey != null && !fields.ContainsKey(m.OutputKey)) Write(m.OutputKey, ProvisionalType(LookupExpr(config, m.ExpressionId)));
        foreach (var sf in config.StateSchema)
        {
            var type = MapType(sf.Type);
            if (!fields.TryGetValue(sf.Name, out var known) || known.Type != type) Write(sf.Name, type);
        }
        var writers = config.Nodes.OfType<ModifyStateNode>().Where(m => m.OutputKey != null).GroupBy(m => m.OutputKey!).ToArray();
        for (var pass = 0; pass <= config.Nodes.OfType<ModifyStateNode>().Count(); pass++)
        {
            var ctx = BuildContext(fields);
            var changed = false;
            foreach (var group in writers)
            {
                var inferred = group.Select(m => LookupExpr(config, m.ExpressionId))
                    .Select(e => e is not null ? ExpressionShapes.Infer(e, ctx) : null).ToArray();
                var shape = inferred.Aggregate(ExpressionShapes.Join);
                if (config.InitialState?.TryGetValue(group.Key, out var initialValue) == true)
                    shape = ExpressionShapes.Join(ExpressionShapes.FromJson(group.Key, initialValue), shape);
                if (shape is null) shape = ExpressionShapes.Field(group.Key, ExprType.Error);
                if (authored.Contains(group.Key))
                {
                    // An explicit scalar type cannot invent record fields or
                    // restore a conflicting array's homogeneous item shape.
                    if (fields[group.Key].Type != shape.Type) continue;
                }
                shape = shape with { Name = group.Key };
                if (!ExpressionShapes.Same(fields[group.Key], shape)) { fields[group.Key] = shape; changed = true; }
            }
            if (!changed) break;
        }
        return fields.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
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
        "record" => ExprType.Record,
        "null" => ExprType.Null,
        _ => ExprType.Number,
    };

    private static TypeCheckContext BuildContext(Dictionary<string, FieldDescriptor> fields) => new()
    {
        ExpectedType = ExprType.Number,
        BoardFields = TypeCheckContext.Default.BoardFields,
        StateFields = fields.Values.ToArray(),
        CellFields = TypeCheckContext.Default.CellFields,
        DecorationTypes = TypeCheckContext.Default.DecorationTypes,
    };
}
