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

        // ── Dict-state mode: reel draw → fast-path evaluator → sink ──────
        //   Reel draws publish a board array and fast-path Map nodes consume it
        //   via the registered IFastPathEvaluator, so this class works on a
        //   Dictionary<string,object?> state (not typed scalar fields).
        var needsDict =
            config.Nodes.OfType<DrawNode>().Any(d => d.BoardStateKey != null || d.DrawWeights is not { Length: > 0 })
            || config.Nodes.OfType<MapNode>().Any();
        if (needsDict)
            return EmitReelFastPath(config, sink);

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

    // ── Dict-state mode: reel draw → fast-path evaluator → sink ───────────

    private EmitResult EmitReelFastPath(GraphConfig config, MetricsSinkNode sink)
    {
        if (sink.WinStateKey != null)
            return EmitResult.Unsupported("reel/fast-path emit requires a data-flow sink (no WinStateKey yet).");

        var incoming = config.Nodes.ToDictionary(n => n.Id, _ => 0);
        var outgoing = config.Nodes.ToDictionary(n => n.Id, _ => new List<Edge>());
        foreach (var e in config.Edges)
        {
            if (incoming.ContainsKey(e.TargetNodeId)) incoming[e.TargetNodeId]++;
            if (outgoing.TryGetValue(e.SourceNodeId, out var lst)) lst.Add(e);
        }

        var entries = config.Nodes.Where(n => incoming[n.Id] == 0).ToArray();
        if (entries is not [DrawNode draw])
            return EmitResult.Unsupported("reel/fast-path emit expects a single reel-draw entry node.");
        if (draw.DrawWeights is { Length: > 0 })
            return EmitResult.Unsupported($"draw '{draw.Id}' is an inline draw, not a reel draw.");
        if (draw.BoardStateKey is not (null or "board"))
            return EmitResult.Unsupported($"draw '{draw.Id}' board key must be the default 'board'.");

        var nodeById = config.Nodes.ToDictionary(n => n.Id, n => n);
        var afterDraw = outgoing[draw.Id];
        if (afterDraw is not [var toMapEdge] || nodeById[toMapEdge.TargetNodeId] is not MapNode map)
            return EmitResult.Unsupported("reel draw must lead to a single fast-path Map node.");
        if (map.TransformId is not { } transformId)
            return EmitResult.Unsupported($"Map node '{map.Id}' has no transformId.");
        if (transformId.StartsWith("plugin:", StringComparison.Ordinal))
            return EmitResult.Unsupported($"Map node '{map.Id}' is a plugin (plugin emit not yet supported).");
        var afterMap = outgoing[map.Id];
        if (afterMap is not [var toSinkEdge] || toSinkEdge.TargetNodeId != sink.Id)
            return EmitResult.Unsupported("fast-path Map node must lead directly to the MetricsSink.");

        // Resolve reel geometry (mirrors GraphCompiler.CompileDraw reel path).
        var reelSet = config.ReelSets.FirstOrDefault();
        if (reelSet is null)
            return EmitResult.Unsupported("reel draw requires a ReelSet.");
        var strips = reelSet.StripIds
            .Select(id => config.ReelStrips.FirstOrDefault(s => s.Id == id))
            .ToArray();
        if (strips.Any(s => s is null || s.Symbols.Length == 0))
            return EmitResult.Unsupported("reel set references missing/empty strips.");

        var rows = config.BoardConfig?.Rows ?? 3;
        var cols = strips.Length;
        long total = 1;
        foreach (var s in strips) total *= s!.Symbols.Length;
        if (total is <= 0 or > 1_000_000)
            return EmitResult.Unsupported("too many reel combinations for a single draw.");

        var source = RenderReelFastPath(strips!, rows, cols, (int)total, transformId);
        return new EmitResult { Supported = true, Source = source, FullTypeName = $"{Namespace}.{ClassName}" };
    }

    private static string RenderReelFastPath(ReelStrip[] strips, int rows, int cols, int total, string transformId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> — emitted by SlotMath.Codegen (G7, reel/fast-path). Do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using SlotMath.Codegen.Runtime;");
        sb.AppendLine("using SlotMath.Core.Mechanics;");
        sb.AppendLine();
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {ClassName} : ICompiledGame");
        sb.AppendLine("{");

        // Reel strips as static data.
        for (var c = 0; c < strips.Length; c++)
        {
            var syms = string.Join(", ", strips[c].Symbols.Select(Quote));
            sb.AppendLine($"    private static readonly string[] __strip{c} = new string[] {{ {syms} }};");
        }
        sb.AppendLine($"    private static readonly string[][] __strips = new string[][] {{ {string.Join(", ", Enumerable.Range(0, strips.Length).Select(c => "__strip" + c))} }};");
        sb.AppendLine($"    private const int __rows = {rows};");
        sb.AppendLine($"    private const int __cols = {cols};");
        sb.AppendLine($"    private static readonly long[] __reelW = __Uniform({total});");
        sb.AppendLine($"    private static readonly string __mech = {Quote(transformId)};");
        sb.AppendLine("    private readonly Dictionary<string, object?> _init = new();");
        sb.AppendLine();

        sb.AppendLine("    public void SetInitial(IReadOnlyDictionary<string, object?> __st)");
        sb.AppendLine("    {");
        sb.AppendLine("        _init.Clear();");
        sb.AppendLine("        foreach (var kv in __st) _init[kv.Key] = kv.Value;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public long RunSpin(IDrawDriver __d)");
        sb.AppendLine("    {");
        sb.AppendLine("        var state = new Dictionary<string, object?>(_init);");
        sb.AppendLine("        int __c = __d.Draw(__reelW);");
        sb.AppendLine("        state[\"board\"] = __Board(__c);");
        sb.AppendLine("        state[\"rows\"] = __rows;");
        sb.AppendLine("        state[\"cols\"] = __cols;");
        sb.AppendLine("        var __ev = EvaluatorRegistry.TryGet(__mech)");
        sb.AppendLine("            ?? throw new InvalidOperationException(\"fast-path evaluator '\" + __mech + \"' is not registered.\");");
        sb.AppendLine("        return __SumWins(__ev.Evaluate(state));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Board decode — mirrors GraphCompiler.BuildFlatFromChoice.
        sb.AppendLine("    private static object?[] __Board(int choice)");
        sb.AppendLine("    {");
        sb.AppendLine("        var flat = new object?[__rows * __cols];");
        sb.AppendLine("        int remaining = choice;");
        sb.AppendLine("        for (int c = __cols - 1; c >= 0; c--)");
        sb.AppendLine("        {");
        sb.AppendLine("            var strip = __strips[c];");
        sb.AppendLine("            int stripLen = strip.Length;");
        sb.AppendLine("            int reelPos = remaining % stripLen;");
        sb.AppendLine("            remaining /= stripLen;");
        sb.AppendLine("            for (int r = 0; r < __rows; r++)");
        sb.AppendLine("                flat[r * __cols + c] = strip[(reelPos + r) % stripLen];");
        sb.AppendLine("        }");
        sb.AppendLine("        return flat;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    private static long[] __Uniform(int n)");
        sb.AppendLine("    {");
        sb.AppendLine("        var a = new long[n];");
        sb.AppendLine("        Array.Fill(a, 1L);");
        sb.AppendLine("        return a;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Win[] → scaled integer (winScale = 1; mirrors SumWins/ToScaledWin).
        sb.AppendLine("    private static long __SumWins(Win[] wins)");
        sb.AppendLine("    {");
        sb.AppendLine("        decimal total = 0m;");
        sb.AppendLine("        foreach (var w in wins) total += w.TotalWin;");
        sb.AppendLine("        return (long)decimal.Round(total, 0, MidpointRounding.ToEven);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
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
                case LoopNode loop:
                    EmitLoop(ctx, loop, outs, indent, stack);
                    return; // the loop handles its own exit-chain continuation.
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

    private static void EmitLoop(Context ctx, LoopNode loop, List<Edge> outs, string indent, HashSet<string> stack)
    {
        // Mirrors GraphCompiler.CompileLoopChain: fixed iteration counter +
        // accumulator state fields (derived as __iter_{id}__ / __wins_{id}__),
        // stop checked BEFORE the body, data-flow value accumulated AFTER.
        var iterKey = $"__iter_{loop.Id}__";
        var winsKey = $"__wins_{loop.Id}__";
        if (!ctx.ByKey.TryGetValue(iterKey, out var iterF) || iterF.Type != ExprType.Number
            || !ctx.ByKey.TryGetValue(winsKey, out var winsF) || winsF.Type != ExprType.Number)
            throw new CodegenUnsupportedException($"loop '{loop.Id}' counter/accumulator fields were not derived.");

        var bodyEdge = outs.FirstOrDefault(e => e.SourcePort == "body")
            ?? throw new CodegenUnsupportedException($"loop '{loop.Id}' has no 'body' port (fallback loop unsupported).");
        var exitEdge = outs.FirstOrDefault(e => e.SourcePort is "exit" or "out");

        var maxIter = loop.MaxIterations is > 0 and <= 10000 ? loop.MaxIterations : 100;

        var stopCond = $"{iterF.Working} >= {maxIter}L";
        if (loop.StopConditionId is { } sid
            && ctx.Config.Expressions is not null
            && ctx.Config.Expressions.TryGetValue(sid, out var stopExpr))
            stopCond = $"({ctx.Expr.Emit(stopExpr)}) || ({stopCond})";

        var bi = indent + "    ";
        ctx.Body.AppendLine($"{indent}{iterF.Working} = 0L;");
        ctx.Body.AppendLine($"{indent}{winsF.Working} = 0L;");
        ctx.Body.AppendLine($"{indent}for (;;)");
        ctx.Body.AppendLine($"{indent}{{");
        ctx.Body.AppendLine($"{bi}if ({stopCond}) break;");
        EmitChain(ctx, bodyEdge.TargetNodeId, bi, stack);  // body in dead-end mode
        ctx.Body.AppendLine($"{bi}{iterF.Working} = {iterF.Working} + 1L;");
        ctx.Body.AppendLine($"{bi}{winsF.Working} = {winsF.Working} + __df;");
        ctx.Body.AppendLine($"{indent}}}");
        ctx.Body.AppendLine($"{indent}__df = {winsF.Working};");

        if (exitEdge is not null)
            EmitChain(ctx, exitEdge.TargetNodeId, indent, stack);
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
