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
        public int FanIndex;
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
            || config.Nodes.OfType<MapNode>().Any()
            || config.Nodes.OfType<DataNode>().Any()
            || (config.Expressions?.Values.Any(IsArrayExpr) ?? false);
        if (needsDict)
            return EmitDictMode(config, sink);

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

    // ═══════════════════════════════════════════════════════════════════════
    //  Dict-state mode — reel/board, fast-path evaluators, and array/fold
    //  expressions.  State is a Dictionary<string,object?> (as the interpreter
    //  uses); scalar sub-expressions reuse ExpressionEmitter via a dict-aware
    //  resolver; fold/aggregate over arrays are emitted as C# for-loops.
    // ═══════════════════════════════════════════════════════════════════════

    private static bool IsArrayExpr(Expression e) => e is FoldExpr or MapExpr or FilterExpr or AggregateExpr;

    private sealed record ReelInfo(ReelStrip[] Strips, int Rows, int Cols, int Total);

    private sealed class DictContext
    {
        public required GraphConfig Config { get; init; }
        public required Dictionary<string, Node> NodeById { get; init; }
        public required Dictionary<string, List<Edge>> Outgoing { get; init; }
        public required Dictionary<string, ExprType> TypeByKey { get; init; }
        public required string SinkId { get; init; }
        public ExpressionEmitter Expr { get; set; } = null!;
        public Dictionary<string, string> Locals { get; } = new(StringComparer.Ordinal);
        public StringBuilder Tables { get; } = new();
        public StringBuilder Body { get; } = new();
        public int DrawIndex;
        public int TmpIndex;
        public ReelInfo? Reel { get; set; }
        public bool UsesPlugin { get; set; }
    }

    private EmitResult EmitDictMode(GraphConfig config, MetricsSinkNode sink)
    {
        var schema = StateSchemaDeriver.Derive(config);
        var typeByKey = schema.ToDictionary(f => f.Name, f => f.Type, StringComparer.Ordinal);

        var incoming = config.Nodes.ToDictionary(n => n.Id, _ => 0);
        var outgoing = config.Nodes.ToDictionary(n => n.Id, _ => new List<Edge>());
        foreach (var e in config.Edges)
        {
            if (incoming.ContainsKey(e.TargetNodeId)) incoming[e.TargetNodeId]++;
            if (outgoing.TryGetValue(e.SourceNodeId, out var lst)) lst.Add(e);
        }

        var entries = config.Nodes.Where(n => incoming[n.Id] == 0).ToArray();
        if (entries.Length != 1)
            return EmitResult.Unsupported($"dict-mode expects exactly one entry node, found {entries.Length}.");

        var ctx = new DictContext
        {
            Config = config,
            NodeById = config.Nodes.ToDictionary(n => n.Id, n => n),
            Outgoing = outgoing,
            TypeByKey = typeByKey,
            SinkId = sink.Id,
        };
        ctx.Expr = new ExpressionEmitter(key => ResolveDict(ctx, key));

        try
        {
            EmitDictChain(ctx, entries[0].Id, "        ", new HashSet<string>(StringComparer.Ordinal));

            string win;
            if (sink.WinStateKey is { } wk)
            {
                if (!typeByKey.TryGetValue(wk, out var wt) || wt != ExprType.Number)
                    return EmitResult.Unsupported($"MetricsSink WinStateKey '{wk}' is not a numeric state field.");
                win = $"__N(state, {Quote(wk)})";
            }
            else
            {
                win = "__df";
            }

            return new EmitResult { Supported = true, Source = RenderDict(ctx, win), FullTypeName = $"{Namespace}.{ClassName}" };
        }
        catch (CodegenUnsupportedException ex)
        {
            return EmitResult.Unsupported(ex.Message);
        }
    }

    private string ResolveDict(DictContext ctx, string key)
    {
        if (ctx.Locals.TryGetValue(key, out var local)) return local;
        if (!ctx.TypeByKey.TryGetValue(key, out var type))
            throw new CodegenUnsupportedException($"reference to unknown state field '{key}'.");
        return type switch
        {
            ExprType.Number => $"__N(state, {Quote(key)})",
            ExprType.String or ExprType.Symbol => $"__S(state, {Quote(key)})",
            ExprType.Boolean => $"__B(state, {Quote(key)})",
            _ => throw new CodegenUnsupportedException($"state field '{key}' has non-scalar type {type} in a scalar expression."),
        };
    }

    private void EmitDictChain(DictContext ctx, string nodeId, string indent, HashSet<string> stack)
    {
        if (nodeId == ctx.SinkId) return;
        if (!stack.Add(nodeId))
            throw new CodegenUnsupportedException($"cyclic data flow at '{nodeId}' (dict-mode loops not yet supported).");

        try
        {
            var node = ctx.NodeById[nodeId];
            var outs = ctx.Outgoing[nodeId];

            switch (node)
            {
                case DrawNode draw:
                    EmitDictDraw(ctx, draw, indent);
                    break;
                case ModifyStateNode modify:
                    EmitDictModify(ctx, modify, indent);
                    break;
                case MapNode map:
                    EmitDictMap(ctx, map, indent);
                    break;
                case PutStateNode put:
                    ctx.Body.AppendLine($"{indent}state[{Quote(put.StateKey)}] = __df;");
                    break;
                case GetStateNode:
                    break;
                default:
                    throw new CodegenUnsupportedException(
                        $"node '{nodeId}' of kind {node.GetType().Name} is not supported in dict-mode (branch/loop over arrays pending).");
            }

            if (outs.Count == 0) return;
            if (outs.Count > 1)
                throw new CodegenUnsupportedException($"node '{nodeId}' has fan-out (not yet supported).");
            EmitDictChain(ctx, outs[0].TargetNodeId, indent, stack);
        }
        finally
        {
            stack.Remove(nodeId);
        }
    }

    private void EmitDictDraw(DictContext ctx, DrawNode draw, string indent)
    {
        if (draw.WeightExpressionId != null)
            throw new CodegenUnsupportedException($"draw '{draw.Id}' uses expression weights (constant/reel only).");

        if (draw.DrawWeights is { Length: > 0 } dw)
        {
            var index = ctx.DrawIndex++;
            var w = string.Join(", ", dw.Select(x => x.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
            var v = string.Join(", ", dw.Select(x => x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L"));
            ctx.Tables.AppendLine($"    private static readonly long[] __w{index} = new long[] {{ {w} }};");
            ctx.Tables.AppendLine($"    private static readonly long[] __v{index} = new long[] {{ {v} }};");
            ctx.Body.AppendLine($"{indent}int __c{index} = __d.Draw(__w{index});");
            ctx.Body.AppendLine($"{indent}__df = __v{index}[__c{index}];");
            if (draw.StateWriteKey is { } wk)
            {
                var ids = string.Join(", ", dw.Select(x => Quote(x.OutcomeId)));
                ctx.Tables.AppendLine($"    private static readonly string[] __o{index} = new string[] {{ {ids} }};");
                ctx.Body.AppendLine($"{indent}state[{Quote(wk)}] = __o{index}[__c{index}];");
            }
            return;
        }

        // Reel draw → board array.
        if (ctx.Reel != null)
            throw new CodegenUnsupportedException("more than one reel draw is not supported.");
        if (draw.BoardStateKey is not (null or "board"))
            throw new CodegenUnsupportedException($"draw '{draw.Id}' board key must be the default 'board'.");

        var reelSet = ctx.Config.ReelSets.FirstOrDefault()
            ?? throw new CodegenUnsupportedException("reel draw requires a ReelSet.");
        var strips = reelSet.StripIds.Select(id => ctx.Config.ReelStrips.FirstOrDefault(s => s.Id == id)).ToArray();
        if (strips.Any(s => s is null || s.Symbols.Length == 0))
            throw new CodegenUnsupportedException("reel set references missing/empty strips.");
        var rows = ctx.Config.BoardConfig?.Rows ?? 3;
        var cols = strips.Length;
        long total = 1;
        foreach (var s in strips) total *= s!.Symbols.Length;
        if (total is <= 0 or > 1_000_000)
            throw new CodegenUnsupportedException("too many reel combinations for a single draw.");

        ctx.Reel = new ReelInfo(strips!, rows, cols, (int)total);
        ctx.Body.AppendLine($"{indent}int __rc = __d.Draw(__reelW);");
        ctx.Body.AppendLine($"{indent}state[\"board\"] = __Board(__rc);");
        ctx.Body.AppendLine($"{indent}state[\"rows\"] = __rows;");
        ctx.Body.AppendLine($"{indent}state[\"cols\"] = __cols;");
    }

    private void EmitDictMap(DictContext ctx, MapNode map, string indent)
    {
        if (map.TransformId is not { } transformId)
            throw new CodegenUnsupportedException($"Map node '{map.Id}' has no transformId.");

        if (transformId.StartsWith("plugin:", StringComparison.Ordinal))
        {
            // Level-c plugin evaluator, resolved from the injected PluginHost.
            var pluginId = transformId["plugin:".Length..];
            ctx.UsesPlugin = true;
            ctx.Body.AppendLine($"{indent}{{");
            ctx.Body.AppendLine($"{indent}    var __pe = __pluginHost!.TryGetEvaluator({Quote(pluginId)})");
            ctx.Body.AppendLine($"{indent}        ?? throw new InvalidOperationException(\"plugin evaluator not found: \" + {Quote(pluginId)});");
            ctx.Body.AppendLine($"{indent}    __df = __SumWins(__pe.Evaluate(state));");
            ctx.Body.AppendLine($"{indent}}}");
            return;
        }

        ctx.Body.AppendLine($"{indent}{{");
        ctx.Body.AppendLine($"{indent}    var __ev = EvaluatorRegistry.TryGet({Quote(transformId)})");
        ctx.Body.AppendLine($"{indent}        ?? throw new InvalidOperationException(\"fast-path evaluator not registered: \" + {Quote(transformId)});");
        ctx.Body.AppendLine($"{indent}    __df = __SumWins(__ev.Evaluate(state));");
        ctx.Body.AppendLine($"{indent}}}");
    }

    private void EmitDictModify(DictContext ctx, ModifyStateNode modify, string indent)
    {
        if (modify.OutputKey is not { } outKey)
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' has no OutputKey.");
        if (modify.ExpressionId is not { } exprId
            || ctx.Config.Expressions is null
            || !ctx.Config.Expressions.TryGetValue(exprId, out var expr))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' references unknown expression '{modify.ExpressionId}'.");
        if (!ctx.TypeByKey.TryGetValue(outKey, out var outType))
            throw new CodegenUnsupportedException($"ModifyState '{modify.Id}' OutputKey '{outKey}' not in schema.");

        switch (expr)
        {
            case FoldExpr fold when outType == ExprType.Number:
                EmitNumberFold(ctx, fold, outKey, indent);
                return;
            case AggregateExpr agg when outType == ExprType.Number:
                EmitNumberAggregate(ctx, agg, outKey, indent);
                return;
            case FoldExpr or MapExpr or FilterExpr or AggregateExpr:
                throw new CodegenUnsupportedException(
                    $"ModifyState '{modify.Id}' produces a non-number array result ({outType}) — array-valued folds/maps pending.");
            default:
                // Scalar expression (reuses ExpressionEmitter via the dict resolver).
                ctx.Body.AppendLine($"{indent}state[{Quote(outKey)}] = {ScalarBoxed(ctx, expr, outType)};");
                return;
        }
    }

    private string ScalarBoxed(DictContext ctx, Expression expr, ExprType outType)
    {
        var c = ctx.Expr.Emit(expr);
        return outType == ExprType.Number ? $"(object)(long)({c})" : $"(object)({c})";
    }

    private void EmitNumberFold(DictContext ctx, FoldExpr fold, string outKey, string indent)
    {
        var arr = $"__arr{ctx.TmpIndex}";
        var it = $"__it{ctx.TmpIndex}";
        var k = $"__k{ctx.TmpIndex}";
        ctx.TmpIndex++;
        var bi = indent + "    ";

        ctx.Body.AppendLine($"{indent}{{");
        ctx.Body.AppendLine($"{bi}long __acc = (long)({ctx.Expr.Emit(fold.Init)});");
        ctx.Body.AppendLine($"{bi}var {arr} = state[{Quote(fold.StateKey)}] as object?[] ?? System.Array.Empty<object?>();");
        ctx.Body.AppendLine($"{bi}for (int {k} = 0; {k} < {arr}.Length; {k}++)");
        ctx.Body.AppendLine($"{bi}{{");
        ctx.Body.AppendLine($"{bi}    {ItemDecl(fold.ItemType, it, $"{arr}[{k}]")}");
        ctx.Locals[fold.AccName] = "__acc";
        ctx.Locals[fold.ItemName] = it;
        if (fold.IndexName is { } ix) ctx.Locals[ix] = $"((long){k})";
        ctx.Body.AppendLine($"{bi}    __acc = (long)({ctx.Expr.Emit(fold.Body)});");
        ctx.Locals.Remove(fold.AccName);
        ctx.Locals.Remove(fold.ItemName);
        if (fold.IndexName is { } ix2) ctx.Locals.Remove(ix2);
        ctx.Body.AppendLine($"{bi}}}");
        ctx.Body.AppendLine($"{bi}state[{Quote(outKey)}] = __acc;");
        ctx.Body.AppendLine($"{indent}}}");
    }

    private void EmitNumberAggregate(DictContext ctx, AggregateExpr agg, string outKey, string indent)
    {
        if (agg.Func is not (AggregateFunc.Count or AggregateFunc.Sum))
            throw new CodegenUnsupportedException($"aggregate '{agg.Func}' not yet supported (Count/Sum only).");

        var arr = $"__arr{ctx.TmpIndex}";
        var it = $"__it{ctx.TmpIndex}";
        var k = $"__k{ctx.TmpIndex}";
        ctx.TmpIndex++;
        var bi = indent + "    ";

        ctx.Body.AppendLine($"{indent}{{");
        ctx.Body.AppendLine($"{bi}long __acc = 0L;");
        ctx.Body.AppendLine($"{bi}var {arr} = state[{Quote(agg.StateKey)}] as object?[] ?? System.Array.Empty<object?>();");
        ctx.Body.AppendLine($"{bi}for (int {k} = 0; {k} < {arr}.Length; {k}++)");
        ctx.Body.AppendLine($"{bi}{{");
        ctx.Body.AppendLine($"{bi}    {ItemDecl(agg.ItemType, it, $"{arr}[{k}]")}");
        ctx.Locals[agg.ItemName] = it;
        var pred = agg.Predicate is { } p ? ctx.Expr.Emit(p) : "true";
        var add = agg.Func == AggregateFunc.Count
            ? "1L"
            : $"(long)({ctx.Expr.Emit(agg.ValueExpr ?? throw new CodegenUnsupportedException("Sum requires a ValueExpr."))})";
        ctx.Locals.Remove(agg.ItemName);
        ctx.Body.AppendLine($"{bi}    if ({pred}) __acc += {add};");
        ctx.Body.AppendLine($"{bi}}}");
        ctx.Body.AppendLine($"{bi}state[{Quote(outKey)}] = __acc;");
        ctx.Body.AppendLine($"{indent}}}");
    }

    private static string ItemDecl(ExprType type, string name, string access) => type switch
    {
        ExprType.Number => $"long {name} = __AsNum({access});",
        ExprType.Boolean => $"bool {name} = __AsBool({access});",
        _ => $"string {name} = __AsStr({access});",
    };

    private static string RenderDict(DictContext ctx, string winExpr)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> — emitted by SlotMath.Codegen (G7, dict-state). Do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0219, IDE0059");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using SlotMath.Codegen.Runtime;");
        sb.AppendLine("using SlotMath.Core.Mechanics;");
        if (ctx.UsesPlugin) sb.AppendLine("using SlotMath.Core.Plugins;");
        sb.AppendLine();
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        var contracts = ctx.UsesPlugin ? "ICompiledGame, IPluginHostAware" : "ICompiledGame";
        sb.AppendLine($"public sealed class {ClassName} : {contracts}");
        sb.AppendLine("{");
        sb.Append(ctx.Tables);
        if (ctx.UsesPlugin)
        {
            sb.AppendLine("    private PluginHost? __pluginHost;");
            sb.AppendLine("    public void SetPluginHost(PluginHost host) => __pluginHost = host;");
        }

        if (ctx.Reel is { } reel)
        {
            for (var c = 0; c < reel.Strips.Length; c++)
            {
                var syms = string.Join(", ", reel.Strips[c].Symbols.Select(Quote));
                sb.AppendLine($"    private static readonly string[] __strip{c} = new string[] {{ {syms} }};");
            }
            sb.AppendLine($"    private static readonly string[][] __strips = new string[][] {{ {string.Join(", ", Enumerable.Range(0, reel.Strips.Length).Select(c => "__strip" + c))} }};");
            sb.AppendLine($"    private const int __rows = {reel.Rows};");
            sb.AppendLine($"    private const int __cols = {reel.Cols};");
            sb.AppendLine($"    private static readonly long[] __reelW = __Uniform({reel.Total});");
        }
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
        sb.AppendLine("        long __df = 0L;");
        sb.Append(ctx.Body);
        sb.AppendLine($"        return {winExpr};");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (ctx.Reel != null)
        {
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
        }

        sb.AppendLine("    private static long __SumWins(Win[] wins)");
        sb.AppendLine("    {");
        sb.AppendLine("        decimal total = 0m;");
        sb.AppendLine("        foreach (var w in wins) total += w.TotalWin;");
        sb.AppendLine("        return (long)decimal.Round(total, 0, MidpointRounding.ToEven);");
        sb.AppendLine("    }");
        sb.AppendLine("    private static long __N(Dictionary<string, object?> s, string k) => s.TryGetValue(k, out var v) ? __AsNum(v) : 0L;");
        sb.AppendLine("    private static string __S(Dictionary<string, object?> s, string k) => s.TryGetValue(k, out var v) ? __AsStr(v) : \"\";");
        sb.AppendLine("    private static bool __B(Dictionary<string, object?> s, string k) => s.TryGetValue(k, out var v) && __AsBool(v);");
        sb.AppendLine("    private static long __AsNum(object? o) => o switch { BigInteger b => (long)b, long l => l, int i => i, _ => 0L };");
        sb.AppendLine("    private static string __AsStr(object? o) => o as string ?? \"\";");
        sb.AppendLine("    private static bool __AsBool(object? o) => o is bool b && b;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Recursive chain walker (mirrors GraphCompiler.GetChain) ───────────

    //  sinkAction is the statement emitted when a path reaches the sink (empty
    //  for the top-level chain, where the win is read afterwards; "__fsumN +=
    //  __df;" for a fan-out branch, where each path's value is summed).
    private static void EmitChain(Context ctx, string nodeId, string indent, HashSet<string> stack, string sinkAction = "")
    {
        if (nodeId == ctx.SinkId)
        {
            if (sinkAction.Length > 0) ctx.Body.AppendLine($"{indent}{sinkAction}");
            return;
        }

        if (!stack.Add(nodeId))
            throw new CodegenUnsupportedException($"cyclic data flow at node '{nodeId}' — loops are not yet supported by the emitter.");

        try
        {
            var node = ctx.NodeById[nodeId];
            var outs = ctx.Outgoing[nodeId];

            switch (node)
            {
                case BranchNode branch:
                    EmitBranch(ctx, branch, outs, indent, stack, sinkAction);
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
                    EmitLoop(ctx, loop, outs, indent, stack, sinkAction);
                    return; // the loop handles its own exit-chain continuation.
                default:
                    throw new CodegenUnsupportedException(
                        $"node '{nodeId}' of kind {node.GetType().Name} is not supported.");
            }

            if (outs.Count == 0)
            {
                // Dead end (loop-body terminal, or a fan-out path that ends without
                // reaching the sink): the data-flow value __df is the path value.
                if (sinkAction.Length > 0) ctx.Body.AppendLine($"{indent}{sinkAction}");
                return;
            }

            if (outs.Count == 1)
            {
                EmitChain(ctx, outs[0].TargetNodeId, indent, stack, sinkAction);
                return;
            }

            // Fan-out (mirrors GraphCompiler fanOut): run every downstream path
            // with the node's value as input (state threads sequentially) and
            // SUM the per-path sink values.
            var sum = $"__fsum{ctx.FanIndex}";
            var saved = $"__fv{ctx.FanIndex}";
            ctx.FanIndex++;
            ctx.Body.AppendLine($"{indent}long {saved} = __df;");
            ctx.Body.AppendLine($"{indent}long {sum} = 0L;");
            foreach (var e in outs)
            {
                ctx.Body.AppendLine($"{indent}__df = {saved};");
                EmitChain(ctx, e.TargetNodeId, indent, stack, $"{sum} += __df;");
            }
            ctx.Body.AppendLine($"{indent}__df = {sum};");
            if (sinkAction.Length > 0) ctx.Body.AppendLine($"{indent}{sinkAction}");
        }
        finally
        {
            stack.Remove(nodeId);
        }
    }

    private static void EmitBranch(
        Context ctx, BranchNode branch, List<Edge> outs, string indent, HashSet<string> stack, string sinkAction)
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
        if (trueEdge is not null) EmitChain(ctx, trueEdge.TargetNodeId, indent + "    ", stack, sinkAction);
        ctx.Body.AppendLine($"{indent}}}");
        ctx.Body.AppendLine($"{indent}else");
        ctx.Body.AppendLine($"{indent}{{");
        if (falseEdge is not null) EmitChain(ctx, falseEdge.TargetNodeId, indent + "    ", stack, sinkAction);
        ctx.Body.AppendLine($"{indent}}}");
    }

    private static void EmitLoop(Context ctx, LoopNode loop, List<Edge> outs, string indent, HashSet<string> stack, string sinkAction)
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
            EmitChain(ctx, exitEdge.TargetNodeId, indent, stack, sinkAction);
        else if (sinkAction.Length > 0)
            ctx.Body.AppendLine($"{indent}{sinkAction}");
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
