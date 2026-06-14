using System.Text;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Codegen.Emit;

// ═══════════════════════════════════════════════════════════════════════════
//  CSharpEmitter — GraphConfig → zero-allocation C# production code (G7).
//
//  Emits a self-contained `ICompiledGame` class for the scalar pipeline subset:
//    entry → Draw(inline weights)* → ModifyState(scalar expr)* → MetricsSink.
//  The recurrence state becomes typed instance fields (derived from graph truth
//  via StateSchemaDeriver — no hand-declared schema); draws read pre-allocated
//  static weight tables, so the RunSpin hot path performs O(1) allocations.
//
//  Anything outside the subset (reel draws, expression weights, loops,
//  branches, arrays/fold, plugins, win scaling) returns an EmitResult with
//  Supported=false and a diagnostic — the emitter never produces code it cannot
//  prove equivalent to the interpreter (invariant 11 / D25).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Compiles a <see cref="GraphConfig"/> to C# source for the supported subset.</summary>
public sealed class CSharpEmitter
{
    private const string Namespace = "SlotMath.Generated";
    private const string ClassName = "GeneratedGame";

    private sealed record Field(string Key, ExprType Type, string Working, string Init);

    public EmitResult Emit(GraphConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var sinks = config.Nodes.OfType<MetricsSinkNode>().ToArray();
        if (sinks.Length != 1)
            return EmitResult.Unsupported($"expected exactly one MetricsSink, found {sinks.Length}.");
        var sink = sinks[0];

        // ── Typed state from graph truth ─────────────────────────────────
        var schema = StateSchemaDeriver.Derive(config);
        var fields = new List<Field>();
        var byKey = new Dictionary<string, Field>(StringComparer.Ordinal);
        foreach (var f in schema)
        {
            if (f.Type is ExprType.Array or ExprType.Weights or ExprType.Error)
                return EmitResult.Unsupported(
                    $"state field '{f.Name}' has unsupported type {f.Type} (scalar fields only — arrays/fold need nested iteration).");
            var san = Sanitize(f.Name);
            var field = new Field(f.Name, f.Type, "_s_" + san, "_i_" + san);
            fields.Add(field);
            byKey[f.Name] = field;
        }

        // ── Linear chain walk: entry → … → sink ──────────────────────────
        var incoming = config.Nodes.ToDictionary(n => n.Id, _ => 0);
        var outgoing = config.Nodes.ToDictionary(n => n.Id, _ => new List<Edge>());
        foreach (var e in config.Edges)
        {
            if (incoming.ContainsKey(e.TargetNodeId)) incoming[e.TargetNodeId]++;
            if (outgoing.TryGetValue(e.SourceNodeId, out var lst)) lst.Add(e);
        }

        var entries = config.Nodes.Where(n => incoming[n.Id] == 0).ToArray();
        if (entries.Length != 1)
            return EmitResult.Unsupported($"expected exactly one entry node, found {entries.Length} (branches/multi-entry unsupported).");

        var resolver = new Func<string, string>(key =>
            byKey.TryGetValue(key, out var f)
                ? f.Working
                : throw new CodegenUnsupportedException($"reference to unknown state field '{key}' (lambda-bound or missing)."));
        var exprEmitter = new ExpressionEmitter(resolver);

        var drawTables = new StringBuilder();
        var body = new StringBuilder();
        var drawIndex = 0;

        try
        {
            var nodeById = config.Nodes.ToDictionary(n => n.Id, n => n);
            var current = entries[0];
            var guard = 0;
            while (current is not MetricsSinkNode)
            {
                if (guard++ > config.Nodes.Length + 1)
                    return EmitResult.Unsupported("graph is cyclic; loops are not supported by the G7 emitter.");

                switch (current)
                {
                    case DrawNode draw:
                        EmitDraw(draw, drawIndex, byKey, drawTables, body);
                        drawIndex++;
                        break;
                    case ModifyStateNode modify:
                        EmitModify(modify, config, byKey, exprEmitter, body);
                        break;
                    default:
                        return EmitResult.Unsupported(
                            $"node '{current.Id}' of kind {current.GetType().Name} is not supported (scalar Draw/ModifyState pipeline only).");
                }

                var outs = outgoing[current.Id];
                if (outs.Count != 1)
                    return EmitResult.Unsupported(
                        $"node '{current.Id}' has {outs.Count} outgoing edges; only a single linear successor is supported.");
                current = nodeById[outs[0].TargetNodeId];
            }

            // ── Win extraction at the sink ───────────────────────────────
            string winExpr;
            if (sink.WinStateKey is { } winKey)
            {
                if (!byKey.TryGetValue(winKey, out var wf) || wf.Type != ExprType.Number)
                    return EmitResult.Unsupported($"MetricsSink WinStateKey '{winKey}' is not a numeric state field.");
                winExpr = wf.Working;
            }
            else
            {
                winExpr = "__df";
            }

            var source = Render(fields, drawTables.ToString(), body.ToString(), winExpr);
            return new EmitResult
            {
                Supported = true,
                Source = source,
                FullTypeName = $"{Namespace}.{ClassName}",
            };
        }
        catch (CodegenUnsupportedException ex)
        {
            return EmitResult.Unsupported(ex.Message);
        }
    }

    private static void EmitDraw(
        DrawNode draw, int index, Dictionary<string, Field> byKey,
        StringBuilder tables, StringBuilder body)
    {
        if (draw.WeightExpressionId != null)
            throw new CodegenUnsupportedException($"draw '{draw.Id}' uses expression weights (constant weights only in G7 v1).");
        if (draw.BoardStateKey != null)
            throw new CodegenUnsupportedException($"draw '{draw.Id}' publishes a board array (reel draws unsupported).");
        if (draw.DrawWeights is not { Length: > 0 })
            throw new CodegenUnsupportedException($"draw '{draw.Id}' has no inline weights (reel draws unsupported).");

        var dw = draw.DrawWeights;
        var w = string.Join(", ", dw.Select(x => x.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
        var v = string.Join(", ", dw.Select(x => x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
        tables.AppendLine($"    private static readonly long[] __w{index} = new long[] {{ {w} }};");
        tables.AppendLine($"    private static readonly long[] __v{index} = new long[] {{ {v} }};");

        body.AppendLine($"        int __c{index} = __d.Draw(__w{index});");
        body.AppendLine($"        __df = __v{index}[__c{index}];");

        if (draw.StateWriteKey is { } writeKey)
        {
            if (!byKey.TryGetValue(writeKey, out var wf) || wf.Type != ExprType.String)
                throw new CodegenUnsupportedException($"draw '{draw.Id}' StateWriteKey '{writeKey}' is not a string state field.");
            var ids = string.Join(", ", dw.Select(x => Quote(x.OutcomeId)));
            tables.AppendLine($"    private static readonly string[] __o{index} = new string[] {{ {ids} }};");
            body.AppendLine($"        {wf.Working} = __o{index}[__c{index}];");
        }
    }

    private static void EmitModify(
        ModifyStateNode modify, GraphConfig config, Dictionary<string, Field> byKey,
        ExpressionEmitter exprEmitter, StringBuilder body)
    {
        if (modify.OutputKey is not { } outputKey)
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' has no OutputKey (legacy path unsupported).");
        if (modify.ExpressionId is not { } exprId
            || config.Expressions is null
            || !config.Expressions.TryGetValue(exprId, out var expr))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' references unknown expression '{modify.ExpressionId}'.");
        if (!byKey.TryGetValue(outputKey, out var outField))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' OutputKey '{outputKey}' was not derived in the state schema.");

        var rhs = exprEmitter.Emit(expr);
        body.AppendLine($"        {outField.Working} = {rhs};");
    }

    private static string Render(IReadOnlyList<Field> fields, string drawTables, string body, string winExpr)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> — emitted by SlotMath.Codegen (G7). Do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using SlotMath.Codegen.Runtime;");
        sb.AppendLine();
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {ClassName} : ICompiledGame");
        sb.AppendLine("{");
        sb.Append(drawTables);
        sb.AppendLine();

        foreach (var f in fields)
        {
            sb.AppendLine($"    private {CsType(f.Type)} {f.Working}{DefaultInit(f.Type)};");
            sb.AppendLine($"    private {CsType(f.Type)} {f.Init}{DefaultInit(f.Type)};");
        }
        sb.AppendLine();

        // SetInitial
        sb.AppendLine("    public void SetInitial(IReadOnlyDictionary<string, object?> __st)");
        sb.AppendLine("    {");
        foreach (var f in fields)
            sb.AppendLine($"        {f.Init} = {Reader(f)};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // RunSpin
        sb.AppendLine("    public long RunSpin(IDrawDriver __d)");
        sb.AppendLine("    {");
        foreach (var f in fields)
            sb.AppendLine($"        {f.Working} = {f.Init};");
        sb.AppendLine("        long __df = 0L;");
        sb.Append(body);
        sb.AppendLine($"        return {winExpr};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Helpers
        sb.AppendLine("    private static long __L(IReadOnlyDictionary<string, object?> s, string k) =>");
        sb.AppendLine("        s.TryGetValue(k, out var v) ? v switch { BigInteger b => (long)b, long l => l, int i => i, _ => 0L } : 0L;");
        sb.AppendLine("    private static string __S(IReadOnlyDictionary<string, object?> s, string k) =>");
        sb.AppendLine("        s.TryGetValue(k, out var v) && v is string str ? str : \"\";");
        sb.AppendLine("    private static bool __B(IReadOnlyDictionary<string, object?> s, string k) =>");
        sb.AppendLine("        s.TryGetValue(k, out var v) && v is bool b && b;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Reader(Field f) => f.Type switch
    {
        ExprType.Number => $"__L(__st, {Quote(f.Key)})",
        ExprType.String or ExprType.Symbol => $"__S(__st, {Quote(f.Key)})",
        ExprType.Boolean => $"__B(__st, {Quote(f.Key)})",
        _ => "default",
    };

    private static string CsType(ExprType t) => t switch
    {
        ExprType.Number => "long",
        ExprType.Boolean => "bool",
        ExprType.String or ExprType.Symbol => "string",
        _ => "object?",
    };

    private static string DefaultInit(ExprType t) => t switch
    {
        ExprType.String or ExprType.Symbol => " = \"\"",
        _ => "",
    };

    private static string Sanitize(string key)
    {
        var sb = new StringBuilder();
        foreach (var ch in key)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
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
