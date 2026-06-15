using System.Text;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Codegen.Emit;

// ═══════════════════════════════════════════════════════════════════════════
//  CSharpEmitter — GraphConfig → C# production code (G7).
//
//  A recursive statement walker that mirrors GraphCompiler.GetChain: it follows
//  edges from the entry node, emitting each node's effect, and recurses through
//  Branch (true/false ports) and the state-plumbing nodes.  The recurrence
//  state becomes typed instance fields derived from graph truth
//  (StateSchemaDeriver); draws read pre-allocated static weight tables, so the
//  RunSpin hot path performs O(1) allocations on the scalar subset.
//
//  Supported: Draw (inline weights), ModifyState (scalar expr), Branch
//  (true/false), PutState, GetState, MetricsSink (WinStateKey or data-flow).
//  Not yet supported (returns a diagnostic, never wrong code — invariant 11 /
//  D25): Loop, fan-out, reel draws, expression weights, arrays/fold, plugins,
//  fast-path evaluators, win scaling ≠ 1.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Compiles a <see cref="GraphConfig"/> to C# source for the supported subset.</summary>
public sealed class CSharpEmitter
{
    private const string Namespace = "SlotMath.Generated";
    private const string ClassName = "GeneratedGame";

    private sealed record Field(string Key, ExprType Type, string Working, string Init);

    // Mutable per-emit state (one emitter call builds one class).
    private sealed class Context
    {
        public required GraphConfig Config { get; init; }
        public required Dictionary<string, Node> NodeById { get; init; }
        public required Dictionary<string, List<Edge>> Outgoing { get; init; }
        public required Dictionary<string, Field> ByKey { get; init; }
        public required string? SinkId { get; init; }
        public required bool HasWinStateKey { get; init; }
        public required ExpressionEmitter Expr { get; init; }
        public StringBuilder Tables { get; } = new();
        public StringBuilder Body { get; } = new();
        public int DrawIndex;
    }

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

        var incoming = config.Nodes.ToDictionary(n => n.Id, _ => 0);
        var outgoing = config.Nodes.ToDictionary(n => n.Id, _ => new List<Edge>());
        foreach (var e in config.Edges)
        {
            if (incoming.ContainsKey(e.TargetNodeId)) incoming[e.TargetNodeId]++;
            if (outgoing.TryGetValue(e.SourceNodeId, out var lst)) lst.Add(e);
        }

        var entries = config.Nodes.Where(n => incoming[n.Id] == 0).ToArray();
        if (entries.Length != 1)
            return EmitResult.Unsupported($"expected exactly one entry node, found {entries.Length} (multi-entry unsupported).");

        var resolver = new Func<string, string>(key =>
            byKey.TryGetValue(key, out var f)
                ? f.Working
                : throw new CodegenUnsupportedException($"reference to unknown state field '{key}' (lambda-bound or missing)."));

        var ctx = new Context
        {
            Config = config,
            NodeById = config.Nodes.ToDictionary(n => n.Id, n => n),
            Outgoing = outgoing,
            ByKey = byKey,
            SinkId = sink.Id,
            HasWinStateKey = sink.WinStateKey is not null,
            Expr = new ExpressionEmitter(resolver),
        };

        try
        {
            EmitChain(ctx, entries[0].Id, "        ", new HashSet<string>(StringComparer.Ordinal));

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

            var source = Render(fields, ctx.Tables.ToString(), ctx.Body.ToString(), winExpr);
            return new EmitResult { Supported = true, Source = source, FullTypeName = $"{Namespace}.{ClassName}" };
        }
        catch (CodegenUnsupportedException ex)
        {
            return EmitResult.Unsupported(ex.Message);
        }
    }

    // ── Recursive chain walker (mirrors GraphCompiler.GetChain) ───────────

    private static void EmitChain(Context ctx, string nodeId, string indent, HashSet<string> stack)
    {
        if (nodeId == ctx.SinkId)
            return; // sink reached — win is read after the whole chain runs.

        if (!stack.Add(nodeId))
            throw new CodegenUnsupportedException($"cyclic data flow at node '{nodeId}' — loops are not yet supported by the emitter.");

        try
        {
            var node = ctx.NodeById[nodeId];
            var outs = ctx.Outgoing[nodeId];

            switch (node)
            {
                case BranchNode branch:
                    EmitBranch(ctx, branch, outs, indent, stack);
                    return; // arms recurse to the sink; nothing follows a branch.

                case DrawNode draw:
                    EmitDraw(ctx, draw, indent);
                    break;
                case ModifyStateNode modify:
                    EmitModify(ctx, modify, indent);
                    break;
                case PutStateNode put:
                    EmitPutState(ctx, put, indent);
                    break;
                case GetStateNode:
                    // Side-effect-free in the chain model: the data-flow value
                    // passes through unchanged (GraphCompiler discards the read).
                    break;
                case LoopNode:
                    throw new CodegenUnsupportedException($"node '{nodeId}' is a Loop — not yet supported by the emitter.");
                default:
                    throw new CodegenUnsupportedException(
                        $"node '{nodeId}' of kind {node.GetType().Name} is not supported.");
            }

            if (outs.Count == 0)
                return; // dead end
            if (outs.Count > 1)
                throw new CodegenUnsupportedException(
                    $"node '{nodeId}' has {outs.Count} outgoing edges (fan-out not yet supported).");

            EmitChain(ctx, outs[0].TargetNodeId, indent, stack);
        }
        finally
        {
            stack.Remove(nodeId);
        }
    }

    private static void EmitBranch(
        Context ctx, BranchNode branch, List<Edge> outs, string indent, HashSet<string> stack)
    {
        if (!ctx.HasWinStateKey)
            throw new CodegenUnsupportedException(
                $"branch '{branch.Id}' requires a MetricsSink WinStateKey (data-flow branch win not yet supported).");

        var trueEdge = outs.FirstOrDefault(e => e.SourcePort == "true");
        var falseEdge = outs.FirstOrDefault(e => e.SourcePort == "false");
        if (trueEdge is null && falseEdge is null)
            throw new CodegenUnsupportedException(
                $"branch '{branch.Id}' has no true/false ports (fallback branch not yet supported).");

        if (branch.ConditionId is not { } condId
            || ctx.Config.Expressions is null
            || !ctx.Config.Expressions.TryGetValue(condId, out var cond))
            throw new CodegenUnsupportedException($"branch '{branch.Id}' references unknown condition '{branch.ConditionId}'.");

        var c = ctx.Expr.Emit(cond);
        ctx.Body.AppendLine($"{indent}if ({c})");
        ctx.Body.AppendLine($"{indent}{{");
        if (trueEdge is not null) EmitChain(ctx, trueEdge.TargetNodeId, indent + "    ", stack);
        ctx.Body.AppendLine($"{indent}}}");
        ctx.Body.AppendLine($"{indent}else");
        ctx.Body.AppendLine($"{indent}{{");
        if (falseEdge is not null) EmitChain(ctx, falseEdge.TargetNodeId, indent + "    ", stack);
        ctx.Body.AppendLine($"{indent}}}");
    }

    private static void EmitDraw(Context ctx, DrawNode draw, string indent)
    {
        if (draw.WeightExpressionId != null)
            throw new CodegenUnsupportedException($"draw '{draw.Id}' uses expression weights (constant weights only).");
        if (draw.BoardStateKey != null)
            throw new CodegenUnsupportedException($"draw '{draw.Id}' publishes a board array (reel draws unsupported).");
        if (draw.DrawWeights is not { Length: > 0 })
            throw new CodegenUnsupportedException($"draw '{draw.Id}' has no inline weights (reel draws unsupported).");

        var index = ctx.DrawIndex++;
        var dw = draw.DrawWeights;
        var w = string.Join(", ", dw.Select(x => x.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
        var v = string.Join(", ", dw.Select(x => x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
        ctx.Tables.AppendLine($"    private static readonly long[] __w{index} = new long[] {{ {w} }};");
        ctx.Tables.AppendLine($"    private static readonly long[] __v{index} = new long[] {{ {v} }};");

        ctx.Body.AppendLine($"{indent}int __c{index} = __d.Draw(__w{index});");
        ctx.Body.AppendLine($"{indent}__df = __v{index}[__c{index}];");

        if (draw.StateWriteKey is { } writeKey)
        {
            if (!ctx.ByKey.TryGetValue(writeKey, out var wf) || wf.Type != ExprType.String)
                throw new CodegenUnsupportedException($"draw '{draw.Id}' StateWriteKey '{writeKey}' is not a string state field.");
            var ids = string.Join(", ", dw.Select(x => Quote(x.OutcomeId)));
            ctx.Tables.AppendLine($"    private static readonly string[] __o{index} = new string[] {{ {ids} }};");
            ctx.Body.AppendLine($"{indent}{wf.Working} = __o{index}[__c{index}];");
        }
    }

    private static void EmitModify(Context ctx, ModifyStateNode modify, string indent)
    {
        if (modify.OutputKey is not { } outputKey)
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' has no OutputKey (legacy path unsupported).");
        if (modify.ExpressionId is not { } exprId
            || ctx.Config.Expressions is null
            || !ctx.Config.Expressions.TryGetValue(exprId, out var expr))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' references unknown expression '{modify.ExpressionId}'.");
        if (!ctx.ByKey.TryGetValue(outputKey, out var outField))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' OutputKey '{outputKey}' was not derived in the state schema.");

        ctx.Body.AppendLine($"{indent}{outField.Working} = {ctx.Expr.Emit(expr)};");
    }

    private static void EmitPutState(Context ctx, PutStateNode put, string indent)
    {
        // PutState stores the data-flow value into its key (GraphCompiler: next[key] = v).
        if (!ctx.ByKey.TryGetValue(put.StateKey, out var f) || f.Type != ExprType.Number)
            throw new CodegenUnsupportedException($"PutState '{put.Id}' key '{put.StateKey}' is not a numeric state field.");
        ctx.Body.AppendLine($"{indent}{f.Working} = __df;");
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

        sb.AppendLine("    public void SetInitial(IReadOnlyDictionary<string, object?> __st)");
        sb.AppendLine("    {");
        foreach (var f in fields)
            sb.AppendLine($"        {f.Init} = {Reader(f)};");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public long RunSpin(IDrawDriver __d)");
        sb.AppendLine("    {");
        foreach (var f in fields)
            sb.AppendLine($"        {f.Working} = {f.Init};");
        sb.AppendLine("        long __df = 0L;");
        sb.Append(body);
        sb.AppendLine($"        return {winExpr};");
        sb.AppendLine("    }");
        sb.AppendLine();

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
