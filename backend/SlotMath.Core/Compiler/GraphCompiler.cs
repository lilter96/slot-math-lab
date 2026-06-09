using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Plugins;
using SlotMath.Core.Random;

namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  GraphCompiler — compiles a visual graph to a runnable Slot program (G14)
//
//  Compilation strategy:
//    - Validate the graph structure
//    - Build adjacency and find entry nodes (no incoming edges)
//    - Recursively compile subgraphs via CompileSubgraph, threading values
//      through the Slot monad (SelectMany chains)
//    - Branch forks route to true/false output ports
//    - Loop nodes use Slot.Loop fixpoint with body/exit ports
//    - Expression-valued ports are compiled using ExpressionCompiler
//    - Plugin references are resolved through PluginHost
// ═══════════════════════════════════════════════════════════════════════════

public sealed class GraphCompiler
{
    private readonly PluginHost? _pluginHost;

    public GraphCompiler(PluginHost? pluginHost = null)
    {
        _pluginHost = pluginHost;
    }

    /// <summary>
    /// Compile a graph config to a runnable Slot program.
    /// </summary>
    public CompileResult Compile(GraphConfig config)
    {
        // Phase 1: Validate
        var errors = GraphValidator.Validate(config, _pluginHost);
        if (errors.Count > 0)
            return CompileResult.Failure(errors);

        // Phase 2: Compile
        try
        {
            var program = BuildProgram(config);
            if (config.Plugins is { Length: > 0 })
                program = Slot.Annotate(program, "compiled-graph", containsPlugin: true);

            return CompileResult.Success(program);
        }
        catch (CompilationException ex)
        {
            return CompileResult.Failure(new CompileError
            {
                NodeId = ex.NodeId,
                Code = ex.ErrorCode,
                Message = ex.Message,
            });
        }
    }

    // ── Program builder ─────────────────────────────────────────────────

    private Slot<Dictionary<string, object?>, BigInteger> BuildProgram(GraphConfig config)
    {
        var nodeMap = config.Nodes.ToDictionary(n => n.Id);
        var incoming = BuildIncomingMap(config);
        var outgoing = BuildOutgoingMap(config);

        // Entry nodes = nodes with no incoming edges
        var entryNodeIds = config.Nodes
            .Where(n => incoming[n.Id].Count == 0)
            .Select(n => n.Id)
            .ToList();

        if (entryNodeIds.Count == 0)
            throw new CompilationException(null, ErrorCodes.InvalidGraph, "Graph has no entry nodes.");

        var sinkId = config.Nodes.OfType<MetricsSinkNode>().First().Id;

        if (entryNodeIds.Count == 1)
        {
            var raw = CompileSubgraph(entryNodeIds[0], sinkId, null, nodeMap, incoming, outgoing, config);
            return raw.SelectMany(v => Slot.Pure<Dictionary<string, object?>, BigInteger>(ExtractBigInteger(v)));
        }

        // Multiple entry nodes: compose all programs and sum
        Slot<Dictionary<string, object?>, BigInteger> composed =
            Slot.Pure<Dictionary<string, object?>, BigInteger>(BigInteger.Zero);
        foreach (var entryId in entryNodeIds)
        {
            var capturedEntryId = entryId;
            var capturedComposed = composed;
            composed = capturedComposed.SelectMany(acc =>
                CompileSubgraph(capturedEntryId, sinkId, null, nodeMap, incoming, outgoing, config)
                    .SelectMany(v =>
                        Slot.Pure<Dictionary<string, object?>, BigInteger>(acc + ExtractBigInteger(v))));
        }

        return composed;
    }

    private static BigInteger ExtractBigInteger(object? value) =>
        value is BigInteger bi ? bi :
        value is Win[] wins ? wins.Aggregate(BigInteger.Zero, (acc, w) => acc + new BigInteger(w.TotalWin)) :
        value is decimal d ? new BigInteger((long)d) :
        BigInteger.Zero;

    private Dictionary<string, List<Edge>> BuildIncomingMap(GraphConfig config)
    {
        var map = new Dictionary<string, List<Edge>>();
        foreach (var node in config.Nodes)
            map[node.Id] = new List<Edge>();
        foreach (var edge in config.Edges)
            map[edge.TargetNodeId].Add(edge);
        return map;
    }

    private Dictionary<string, List<Edge>> BuildOutgoingMap(GraphConfig config)
    {
        var map = new Dictionary<string, List<Edge>>();
        foreach (var node in config.Nodes)
            map[node.Id] = new List<Edge>();
        foreach (var edge in config.Edges)
            map[edge.SourceNodeId].Add(edge);
        return map;
    }

    // ── Recursive subgraph compiler ─────────────────────────────────────

    /// <summary>
    /// Recursively compile the subgraph starting at nodeId, threading inputValue forward.
    /// Stops when nodeId==sinkId (returns value), or when there are no more outgoing edges (dead end).
    /// sinkId may be null for loop bodies (compile to dead end).
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> CompileSubgraph(
        string nodeId,
        string? sinkId,
        object? inputValue,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing,
        GraphConfig config)
    {
        var node = nodeMap[nodeId];

        // Base case: reached sink
        if (sinkId != null && nodeId == sinkId)
            return Slot.Pure<Dictionary<string, object?>, object?>(inputValue);

        var inputs = inputValue != null
            ? new Dictionary<string, object?> { ["in"] = inputValue }
            : new Dictionary<string, object?>();

        // Special: Branch with true/false output ports
        if (node is BranchNode branch)
        {
            var trueEdge = outgoing[nodeId].FirstOrDefault(e => e.SourcePort == "true");
            var falseEdge = outgoing[nodeId].FirstOrDefault(e => e.SourcePort == "false");
            if (trueEdge != null || falseEdge != null)
            {
                return CompileBranchFork(branch, inputValue, sinkId, trueEdge, falseEdge,
                    nodeMap, incoming, outgoing, config);
            }
        }

        // Special: Loop with body/exit output ports
        if (node is LoopNode loop)
        {
            var bodyEdge = outgoing[nodeId].FirstOrDefault(e => e.SourcePort == "body");
            if (bodyEdge != null)
            {
                var exitEdge = outgoing[nodeId].FirstOrDefault(e =>
                    e.SourcePort == "exit" || e.SourcePort == "out");
                return CompileLoopNode(loop, inputValue, sinkId, bodyEdge, exitEdge,
                    nodeMap, incoming, outgoing, config);
            }
        }

        // Regular node: compile then follow single outgoing edge
        var nodeSlot = CompileNodeOutput(node, inputs, config, nodeMap, incoming, outgoing);

        var nextEdges = outgoing[nodeId];
        if (!nextEdges.Any())
            return nodeSlot; // dead end (loop body terminal)

        var nextNodeId = nextEdges.First().TargetNodeId;

        // State operations are side-effects: the data-flow value passes through unchanged.
        if (node is GetStateNode or PutStateNode or ModifyStateNode)
        {
            var capturedInput = inputValue;
            return nodeSlot.SelectMany(_ =>
                CompileSubgraph(nextNodeId, sinkId, capturedInput, nodeMap, incoming, outgoing, config));
        }

        return nodeSlot.SelectMany(value =>
            CompileSubgraph(nextNodeId, sinkId, value, nodeMap, incoming, outgoing, config));
    }

    // ── Branch fork compiler ────────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileBranchFork(
        BranchNode node,
        object? inputValue,
        string? sinkId,
        Edge? trueEdge,
        Edge? falseEdge,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing,
        GraphConfig config)
    {
        Func<Board?, Dictionary<string, object?>, bool>? compiledCond = null;
        if (node.ConditionId != null
            && config.Expressions != null
            && config.Expressions.TryGetValue(node.ConditionId, out var condExpr))
        {
            compiledCond = ExpressionCompiler.CompileBoolean(condExpr);
        }

        var capturedInput = inputValue;
        var capturedCond = compiledCond;
        return Slot.GetState<Dictionary<string, object?>>()
            .SelectMany(state =>
            {
                var board = capturedInput as Board;
                bool pass = capturedCond?.Invoke(board, state) ?? false;

                if (pass && trueEdge != null)
                    return CompileSubgraph(trueEdge.TargetNodeId, sinkId, capturedInput,
                        nodeMap, incoming, outgoing, config);
                if (!pass && falseEdge != null)
                    return CompileSubgraph(falseEdge.TargetNodeId, sinkId, capturedInput,
                        nodeMap, incoming, outgoing, config);

                return Slot.Pure<Dictionary<string, object?>, object?>((object?)BigInteger.Zero);
            });
    }

    // ── Loop node compiler (proper Slot.Loop fixpoint) ──────────────────

    private Slot<Dictionary<string, object?>, object?> CompileLoopNode(
        LoopNode node,
        object? inputValue,
        string? sinkId,
        Edge bodyEdge,
        Edge? exitEdge,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing,
        GraphConfig config)
    {
        var iterKey = $"__iter_{node.Id}__";
        var winsKey = $"__wins_{node.Id}__";
        var maxIter = node.MaxIterations > 0 && node.MaxIterations <= 10000
            ? node.MaxIterations : 100;
        var capturedMaxIter = maxIter;
        var capturedIterKey = iterKey;
        var capturedWinsKey = winsKey;

        // Build stop condition: MaxIterations safety cap + optional expression
        Func<Dictionary<string, object?>, bool> stopFn = s =>
        {
            var iter = s.TryGetValue(capturedIterKey, out var v) && v is int i ? i : 0;
            return iter >= capturedMaxIter;
        };

        if (node.StopConditionId != null
            && config.Expressions != null
            && config.Expressions.TryGetValue(node.StopConditionId, out var stopExpr))
        {
            var compiledStop = ExpressionCompiler.CompileBoolean(stopExpr);
            var prevStop = stopFn;
            stopFn = s => prevStop(s) || compiledStop(null, s);
        }

        var capturedStop = stopFn;
        var bodyStartId = bodyEdge.TargetNodeId;

        // Body program: compile body subgraph + accumulate result + increment counter
        Slot<Dictionary<string, object?>, Unit> body =
            CompileSubgraph(bodyStartId, null, null, nodeMap, incoming, outgoing, config)
                .SelectMany(result =>
                    Slot.Modify<Dictionary<string, object?>>(s =>
                    {
                        var next = new Dictionary<string, object?>(s);
                        var iter = s.TryGetValue(capturedIterKey, out var v) && v is int i ? i : 0;
                        next[capturedIterKey] = iter + 1;
                        if (result is BigInteger val)
                        {
                            var acc = s.TryGetValue(capturedWinsKey, out var w) && w is BigInteger ew
                                ? ew : BigInteger.Zero;
                            next[capturedWinsKey] = acc + val;
                        }
                        else if (result is Win[] wins)
                        {
                            var total = wins.Aggregate(BigInteger.Zero,
                                (a, win) => a + new BigInteger(win.TotalWin));
                            var acc = s.TryGetValue(capturedWinsKey, out var w) && w is BigInteger ew
                                ? ew : BigInteger.Zero;
                            next[capturedWinsKey] = acc + total;
                        }

                        return next;
                    }));

        // Initialize state, run loop, then continue or return wins
        return Slot.Modify<Dictionary<string, object?>>(s =>
            {
                var next = new Dictionary<string, object?>(s);
                next[capturedIterKey] = 0;
                next[capturedWinsKey] = BigInteger.Zero;
                return next;
            })
            .SelectMany(_ => Slot.Loop<Dictionary<string, object?>>(capturedStop, body))
            .SelectMany(_ =>
            {
                if (exitEdge != null)
                    return CompileSubgraph(exitEdge.TargetNodeId, sinkId,
                        (object?)BigInteger.Zero, nodeMap, incoming, outgoing, config);

                return Slot.GetState<Dictionary<string, object?>, object?>(s =>
                    s.TryGetValue(capturedWinsKey, out var w) ? w : (object?)BigInteger.Zero);
            });
    }

    // ── CompileNodeOutput dispatch ───────────────────────────────────────

    /// <summary>
    /// Compile a single node's output from its input values.
    /// Returns a Slot that produces the node's primary output value.
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> CompileNodeOutput(
        Node node,
        Dictionary<string, object?> inputs,
        GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing)
    {
        return node switch
        {
            DrawNode d => CompileDraw(d, config),
            MapNode m => CompileMap(m, inputs, config),
            GetStateNode gs => CompileGetState(gs),
            PutStateNode ps => CompilePutState(ps, inputs),
            ModifyStateNode ms => CompileModifyState(ms, config),
            BranchNode b => CompileBranch(b, inputs, config, nodeMap, incoming, outgoing),
            LoopNode l => CompileLegacyLoop(l, config, nodeMap, incoming, outgoing),
            MetricsSinkNode s => CompileSink(s, inputs),
            _ => Slot.Pure<Dictionary<string, object?>, object?>(null),
        };
    }

    // ── Primitive node compilers ────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileDraw(
        DrawNode drawNode, GraphConfig config)
    {
        // Level (b): expression-driven weights + inline outcome values
        if (drawNode.WeightExpressionId != null
            && config.Expressions != null
            && config.Expressions.TryGetValue(drawNode.WeightExpressionId, out var weightExpr)
            && drawNode.DrawWeights is { Length: > 0 })
        {
            var dw = drawNode.DrawWeights;
            var stateWriteKey = drawNode.StateWriteKey;
            var compiledWeights = ExpressionCompiler.CompileWeights(weightExpr);

            if (stateWriteKey != null)
            {
                var capturedDw = dw;
                var capturedKey = stateWriteKey;
                return Slot.Draw<Dictionary<string, object?>>(state => compiledWeights(state))
                    .SelectMany(idx =>
                        Slot.Modify<Dictionary<string, object?>>(s =>
                            {
                                var next = new Dictionary<string, object?>(s);
                                next[capturedKey] = capturedDw[idx].OutcomeId;
                                return next;
                            })
                            .SelectMany(_ =>
                                Slot.Pure<Dictionary<string, object?>, object?>(
                                    (object?)new BigInteger(capturedDw[idx].Value))));
            }

            return Slot.Draw<Dictionary<string, object?>, object?>(
                state => compiledWeights(state),
                idx => (object?)new BigInteger(dw[idx].Value));
        }

        // Level (a): inline weighted draw (custom outcomes, no ReelSets required)
        if (drawNode.DrawWeights is { Length: > 0 })
        {
            var dw = drawNode.DrawWeights;
            var stateWriteKey = drawNode.StateWriteKey;
            var inlineWeights = WeightSet.FromIntegers(dw.Select(w => w.Weight).ToArray());

            if (stateWriteKey != null)
            {
                var capturedDw = dw;
                var capturedKey = stateWriteKey;
                return Slot.Draw<Dictionary<string, object?>>(_ => inlineWeights)
                    .SelectMany(idx =>
                        Slot.Modify<Dictionary<string, object?>>(s =>
                            {
                                var next = new Dictionary<string, object?>(s);
                                next[capturedKey] = capturedDw[idx].OutcomeId;
                                return next;
                            })
                            .SelectMany(_ =>
                                Slot.Pure<Dictionary<string, object?>, object?>(
                                    (object?)new BigInteger(capturedDw[idx].Value))));
            }

            return Slot.Draw<Dictionary<string, object?>, object?>(
                _ => inlineWeights,
                idx => (object?)new BigInteger(dw[idx].Value));
        }

        // Level (a): reel-strip draw (slot machine)
        var reelSet = config.ReelSets.FirstOrDefault()
            ?? throw new CompilationException(drawNode.Id, ErrorCodes.InvalidGraph,
                "No ReelSet defined in graph config. Add reel strips or configure inline draw weights.");

        var strips = reelSet.StripIds
            .Select(sid => config.ReelStrips.FirstOrDefault(s => s.Id == sid))
            .Where(s => s != null)
            .Select(s => s!)
            .ToArray();

        if (strips.Length == 0)
            throw new CompilationException(drawNode.Id, ErrorCodes.InvalidGraph,
                $"ReelSet '{reelSet.Id}' references no valid reel strips.");

        var rows = config.BoardConfig?.Rows ?? 3;

        // Build weight set from all possible reel stop combinations
        var weights = BuildReelWeights(strips);
        var stripArray = strips;
        var rowsCapture = rows;

        return Slot.Draw<Dictionary<string, object?>, object?>(
            _ => weights,
            choiceIndex =>
            {
                var board = BuildBoardFromChoice(choiceIndex, stripArray, rowsCapture);
                return board;
            }
        );
    }

    private static WeightSet BuildReelWeights(ReelStrip[] strips)
    {
        // Each outcome is an index into the product space of reel strip positions
        int totalOutcomes = strips.Aggregate(1, (acc, s) => acc * s.Symbols.Length);

        if (totalOutcomes > 1_000_000)
            throw new InvalidOperationException(
                $"Too many reel combinations ({totalOutcomes}) for exact evaluation.");

        var weightArray = new long[totalOutcomes];
        Array.Fill(weightArray, 1L); // uniform weights by default
        return WeightSet.FromIntegers(weightArray);
    }

    private static Board BuildBoardFromChoice(
        int choiceIndex, ReelStrip[] strips, int rows)
    {
        int cols = strips.Length;
        var board = new Board(rows, cols);

        // Decode the choice index into a reel position for each column
        int remaining = choiceIndex;
        for (int c = cols - 1; c >= 0; c--)
        {
            int stripLen = strips[c].Symbols.Length;
            int reelPos = remaining % stripLen;
            remaining /= stripLen;

            for (int r = 0; r < rows; r++)
            {
                int stripPos = (reelPos + r) % stripLen;
                var symbolId = strips[c].Symbols[stripPos];
                var cell = new BoardCell { Symbols = new[] { symbolId } };
                board = board.SetCell(r, c, cell);
            }
        }

        return board;
    }

    // ── Map node compiler ───────────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileMap(
        MapNode mapNode,
        Dictionary<string, object?> inputs,
        GraphConfig config)
    {
        // Extract the board from inputs
        var board = inputs.Values.OfType<Board>().FirstOrDefault();

        // Compile expression-valued inputs
        var expressionValues = new Dictionary<string, BigInteger>();
        foreach (var (portName, port) in mapNode.Inputs)
        {
            if (port.DefaultValue != null && !inputs.ContainsKey(portName))
            {
                var compiled = ExpressionCompiler.CompileNumber(port.DefaultValue);
                var value = compiled(board, inputs);
                expressionValues[portName] = value;
            }
        }

        // Resolve and invoke the evaluator/transform
        if (mapNode.TransformId != null)
        {
            return CompileWithTransform(mapNode.TransformId, board, expressionValues, mapNode.Id);
        }

        // No transform specified: check port types to infer behavior
        var hasBoardInput = mapNode.Inputs.Values.Any(p => p.Type == PortType.Board);
        var hasWinsOutput = mapNode.Outputs.Values.Any(p => p.Type == PortType.Wins);

        if (hasBoardInput && hasWinsOutput && board != null)
        {
            // Board → Wins: return empty wins (no evaluator specified)
            return Slot.Pure<Dictionary<string, object?>, object?>(Array.Empty<Win>());
        }

        // Pass through the board if nothing else matches
        return Slot.Pure<Dictionary<string, object?>, object?>(board!);
    }

    private Slot<Dictionary<string, object?>, object?> CompileWithTransform(
        string transformId, Board? board,
        Dictionary<string, BigInteger> expressionValues, string nodeId)
    {
        // Plugin reference: "plugin:pluginId"
        if (transformId.StartsWith("plugin:"))
        {
            var pluginId = transformId["plugin:".Length..];
            var evaluator = _pluginHost?.TryGetEvaluator(pluginId);
            if (evaluator == null)
                throw new CompilationException(nodeId, ErrorCodes.PluginNotFound,
                    $"Plugin '{pluginId}' not found.");

            var wins = evaluator.Evaluate(board!, new Dictionary<string, object?>());
            return Slot.Pure<Dictionary<string, object?>, object?>(ApplyExpressions(wins, expressionValues));
        }

        // Evaluator registry
        var regEval = EvaluatorRegistry.TryGet(transformId);
        if (regEval != null)
        {
            var wins = regEval.Evaluate(board!, new Dictionary<string, object?>());
            return Slot.Pure<Dictionary<string, object?>, object?>(ApplyExpressions(wins, expressionValues));
        }

        // Transform registry
        var transform = TransformRegistry.TryGet(transformId);
        if (transform != null)
        {
            var (newBoard, _) = transform.Apply(board!, new Dictionary<string, object?>());
            return Slot.Pure<Dictionary<string, object?>, object?>(newBoard);
        }

        throw new CompilationException(nodeId, ErrorCodes.MissingTransform,
            $"Transform/evaluator '{transformId}' not found.");
    }

    private static Win[] ApplyExpressions(Win[] wins, Dictionary<string, BigInteger> expressionValues)
    {
        if (expressionValues.Count == 0) return wins;

        var multiplier = expressionValues.Values.Aggregate(BigInteger.One, (acc, v) => acc * v);
        if (multiplier == BigInteger.One) return wins;

        return wins.Select(w =>
        {
            var w2 = new Win
            {
                SymbolId = w.SymbolId,
                Count = w.Count,
                Positions = w.Positions,
                Payout = w.Payout,
                Multiplier = w.Multiplier * (decimal)multiplier,
                EvaluatorName = w.EvaluatorName,
            };
            return w2;
        }).ToArray();
    }

    /// <summary>
    /// Evaluate an expression, handling references to named expressions via
    /// the state.expressions.X pattern.
    /// </summary>
    private static BigInteger EvaluateExpression(
        Expression expr, Board? board, GraphConfig config)
    {
        // Handle FieldAccessExpr that references state.expressions.X
        if (expr is FieldAccessExpr fa && fa.Target == "state" && fa.Path.Length >= 2 && fa.Path[0] == "expressions")
        {
            var exprName = fa.Path[1];
            if (config.Expressions != null && config.Expressions.TryGetValue(exprName, out var referencedExpr))
            {
                // Recursively evaluate the referenced expression
                var compiled = ExpressionCompiler.CompileNumber(referencedExpr);
                return compiled(board, new Dictionary<string, object?>());
            }
        }

        // Default: compile and evaluate directly
        var directCompiled = ExpressionCompiler.CompileNumber(expr);
        return directCompiled(board, new Dictionary<string, object?>());
    }

    // ── State node compilers ────────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileGetState(GetStateNode node)
    {
        return Slot.GetState<Dictionary<string, object?>, object?>(state =>
        {
            var key = node.StateKey ?? "__default__";
            state.TryGetValue(key, out var value);
            return Slot.Pure<Dictionary<string, object?>, object?>(value);
        });
    }

    private Slot<Dictionary<string, object?>, object?> CompilePutState(
        PutStateNode node, Dictionary<string, object?> inputs)
    {
        var value = inputs.Values.FirstOrDefault();
        return Slot.GetState<Dictionary<string, object?>>().SelectMany(state =>
        {
            state[node.StateKey] = value;
            return Slot.PutState<Dictionary<string, object?>>(state);
        }).SelectMany(_ => Slot.Pure<Dictionary<string, object?>, object?>(value!));
    }

    private Slot<Dictionary<string, object?>, object?> CompileModifyState(
        ModifyStateNode node, GraphConfig config)
    {
        // Modify state as side-effect, return null (caller passes through prevOutput).
        return Slot.Modify<Dictionary<string, object?>>(state =>
        {
            if (node.ExpressionId != null && config.Expressions != null &&
                config.Expressions.TryGetValue(node.ExpressionId, out var expr))
            {
                var compiled = ExpressionCompiler.CompileNumber(expr);
                var val = compiled(null, state);
                state["__modified__"] = val;
            }

            return state;
        }).SelectMany(_ => Slot.Pure<Dictionary<string, object?>, object?>(null!));
    }

    // ── Branch / Loop compilers ─────────────────────────────────────────

    /// <summary>
    /// Fallback branch compiler (used via CompileNodeOutput when no true/false ports).
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> CompileBranch(
        BranchNode node,
        Dictionary<string, object?> inputs,
        GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing)
    {
        var value = inputs.Values.FirstOrDefault();

        if (node.ConditionId != null
            && config.Expressions != null
            && config.Expressions.TryGetValue(node.ConditionId, out var condExpr))
        {
            var compiledCond = ExpressionCompiler.CompileBoolean(condExpr);
            var capturedValue = value;
            return Slot.GetState<Dictionary<string, object?>>()
                .SelectMany(state =>
                {
                    var board = capturedValue as Board;
                    bool pass = compiledCond(board, state);
                    return Slot.Pure<Dictionary<string, object?>, object?>(
                        pass ? capturedValue : (object?)BigInteger.Zero);
                });
        }

        return Slot.Pure<Dictionary<string, object?>, object?>(value);
    }

    /// <summary>
    /// Legacy loop compiler (used via CompileNodeOutput when no body port — backward compat).
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> CompileLegacyLoop(
        LoopNode node,
        GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing)
    {
        int maxIter = node.MaxIterations > 0 && node.MaxIterations <= 500
            ? node.MaxIterations : 5;
        var capturedMaxIter = maxIter;

        var iterKey2 = $"__legacyiter_{node.Id}__";
        Func<Dictionary<string, object?>, bool> stopFn = s =>
        {
            var iter = s.TryGetValue(iterKey2, out var v) && v is int i ? i : 0;
            return iter >= capturedMaxIter;
        };

        if (node.StopConditionId != null
            && config.Expressions != null
            && config.Expressions.TryGetValue(node.StopConditionId, out var stopExpr))
        {
            var compiledStop = ExpressionCompiler.CompileBoolean(stopExpr);
            var prevStop2 = stopFn;
            stopFn = s => prevStop2(s) || compiledStop(null, s);
        }

        var capturedStop2 = stopFn;
        var capturedIterKey2 = iterKey2;

        // Find the body node from incoming edges
        var bodyEdges = incoming[node.Id];
        if (!bodyEdges.Any())
            return Slot.Pure<Dictionary<string, object?>, object?>((object?)BigInteger.Zero);

        var bodyNodeId = bodyEdges.First().SourceNodeId;
        var bodyNode = nodeMap[bodyNodeId];

        var winsKey2 = $"__legacywins_{node.Id}__";
        var capturedWinsKey2 = winsKey2;

        Slot<Dictionary<string, object?>, Unit> body2 =
            CompileNodeOutput(bodyNode, new Dictionary<string, object?>(),
                config, nodeMap, incoming, outgoing)
                .SelectMany(result =>
                    Slot.Modify<Dictionary<string, object?>>(s =>
                    {
                        var next = new Dictionary<string, object?>(s);
                        var iter = s.TryGetValue(capturedIterKey2, out var v) && v is int i ? i : 0;
                        next[capturedIterKey2] = iter + 1;
                        var val = result is BigInteger bi ? bi : BigInteger.Zero;
                        var acc = s.TryGetValue(capturedWinsKey2, out var w) && w is BigInteger ew
                            ? ew : BigInteger.Zero;
                        next[capturedWinsKey2] = acc + val;
                        return next;
                    }));

        return Slot.Modify<Dictionary<string, object?>>(s =>
            {
                var next = new Dictionary<string, object?>(s);
                next[capturedIterKey2] = 0;
                next[capturedWinsKey2] = BigInteger.Zero;
                return next;
            })
            .SelectMany(_ => Slot.Loop<Dictionary<string, object?>>(capturedStop2, body2))
            .SelectMany(_ =>
                Slot.GetState<Dictionary<string, object?>, object?>(s =>
                    s.TryGetValue(capturedWinsKey2, out var w) ? w : (object?)BigInteger.Zero));
    }

    // ── Sink compiler ───────────────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileSink(
        MetricsSinkNode sinkNode, Dictionary<string, object?> inputs)
    {
        // The sink's output is the accumulated wins (or whatever feeds into it)
        var value = inputs.Values.FirstOrDefault();
        return Slot.Pure<Dictionary<string, object?>, object?>(value);
    }
}

// ═══════════════════════════════════════════════════════════════════════════

internal sealed class CompilationException : Exception
{
    public string? NodeId { get; }
    public string ErrorCode { get; }

    public CompilationException(string? nodeId, string errorCode, string message)
        : base(message)
    {
        NodeId = nodeId;
        ErrorCode = errorCode;
    }
}
