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
//    - Build adjacency and find entry nodes
//    - Walk forward from entry to MetricsSink along the unique path
//    - Chain nodes via SelectMany, threading values through the Slot monad
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

        // Find entry nodes (no incoming edges)
        var entryNodes = config.Nodes
            .Where(n => incoming[n.Id].Count == 0)
            .Select(n => n.Id)
            .ToArray();

        if (entryNodes.Length == 0)
            throw new CompilationException(null, ErrorCodes.InvalidGraph,
                "Graph has no entry nodes.");

        var sinkNode = config.Nodes.OfType<MetricsSinkNode>().First();

        // Walk from entry to sink along the unique path, compiling each node
        return CompileLinearPath(entryNodes[0], sinkNode.Id, nodeMap, incoming, outgoing, config);
    }

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

    /// <summary>
    /// Compile a linear path from entryNodeId to sinkNodeId.
    /// Each node's output feeds into the next node via SelectMany.
    /// </summary>
    private Slot<Dictionary<string, object?>, BigInteger> CompileLinearPath(
        string entryNodeId,
        string sinkNodeId,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing,
        GraphConfig config)
    {
        // Find the unique path from entry to sink
        var path = FindPath(entryNodeId, sinkNodeId, outgoing, nodeMap);
        if (path == null)
            throw new CompilationException(entryNodeId, ErrorCodes.InvalidGraph,
                $"No path from entry node '{entryNodeId}' to MetricsSink.");

        // Compile the first node (entry) — produces object?
        var program = CompileEntryNode(path[0], config, nodeMap, incoming, outgoing);

        // Chain through intermediate nodes
        for (int i = 1; i < path.Count; i++)
        {
            var nodeId = path[i];
            program = ChainNode(program, nodeId, config, nodeMap, incoming, outgoing);
        }

        // The last node is the sink — extract total
        return program.SelectMany(lastOutput =>
        {
            if (lastOutput is Win[] wins)
            {
                var total = wins.Aggregate(BigInteger.Zero, (acc, w) => acc + new BigInteger(w.TotalWin));
                return Slot.Pure<Dictionary<string, object?>, BigInteger>(total);
            }
            if (lastOutput is BigInteger bi)
                return Slot.Pure<Dictionary<string, object?>, BigInteger>(bi);
            if (lastOutput is decimal d)
                return Slot.Pure<Dictionary<string, object?>, BigInteger>(new BigInteger(d));
            return Slot.Pure<Dictionary<string, object?>, BigInteger>(BigInteger.Zero);
        });
    }

    /// <summary>
    /// Find the unique path from start to end using BFS.
    /// </summary>
    private List<string>? FindPath(
        string start, string end,
        Dictionary<string, List<Edge>> outgoing,
        Dictionary<string, Node> nodeMap)
    {
        var queue = new Queue<List<string>>();
        queue.Enqueue(new List<string> { start });
        var visited = new HashSet<string> { start };

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var last = path[^1];

            if (last == end)
                return path;

            foreach (var edge in outgoing[last])
            {
                if (visited.Add(edge.TargetNodeId))
                {
                    var newPath = new List<string>(path) { edge.TargetNodeId };
                    queue.Enqueue(newPath);
                }
            }
        }

        return null;
    }

    // ── Node compilation ────────────────────────────────────────────────

    /// <summary>
    /// Compile an entry node — the first node in the graph.
    /// Returns a Slot that produces the node's output value.
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> CompileEntryNode(
        string nodeId,
        GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing)
    {
        var node = nodeMap[nodeId];
        return CompileNodeOutput(node, new Dictionary<string, object?>(), config, nodeMap, incoming, outgoing);
    }

    /// <summary>
    /// Chain a previously compiled program into the next node.
    /// The previous output becomes the input to the next node.
    /// </summary>
    private Slot<Dictionary<string, object?>, object?> ChainNode(
        Slot<Dictionary<string, object?>, object?> previous,
        string nodeId,
        GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming,
        Dictionary<string, List<Edge>> outgoing)
    {
        return previous.SelectMany(prevOutput =>
        {
            var node = nodeMap[nodeId];

            // State operations are side-effects — execute them and pass through prevOutput.
            if (node is GetStateNode or PutStateNode or ModifyStateNode)
            {
                // State ops receive the current data flow value and return it unchanged
                // after performing their state side-effect.
                return CompileNodeOutput(node, new Dictionary<string, object?>
                {
                    ["__data__"] = prevOutput
                }, config, nodeMap, incoming, outgoing)
                .SelectMany(_ => Slot.Pure<Dictionary<string, object?>, object?>(prevOutput));
            }

            // Build input values for this node from the previous output and expressions
            var inputValues = new Dictionary<string, object?>();

            // Map the previous output to the appropriate input port
            var incEdges = incoming[nodeId];
            foreach (var edge in incEdges)
            {
                inputValues[edge.TargetPort] = prevOutput;
            }

            // Compile expression-valued ports
            foreach (var (portName, port) in node.Inputs)
            {
                if (port.DefaultValue != null && !inputValues.ContainsKey(portName))
                {
                    var board = prevOutput as Board;
                    var value = EvaluateExpression(port.DefaultValue, board, config);
                    inputValues[portName] = value;
                }
            }

            return CompileNodeOutput(node, inputValues, config, nodeMap, incoming, outgoing);
        });
    }

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
            LoopNode l => CompileLoop(l, config, nodeMap, incoming, outgoing),
            MetricsSinkNode s => CompileSink(s, inputs),
            _ => Slot.Pure<Dictionary<string, object?>, object?>(null),
        };
    }

    // ── Primitive node compilers ────────────────────────────────────────

    private Slot<Dictionary<string, object?>, object?> CompileDraw(
        DrawNode drawNode, GraphConfig config)
    {
        // ── Inline weighted draw (custom outcomes, no ReelSets required) ──
        if (drawNode.DrawWeights is { Length: > 0 })
        {
            var dw = drawNode.DrawWeights;
            var weights = WeightSet.FromIntegers(dw.Select(w => w.Weight).ToArray());
            return Slot.Draw<Dictionary<string, object?>, object?>(
                _ => weights,
                idx => (object?)new BigInteger(dw[idx].Value));
        }

        // ── Reel-strip draw (slot machine) ──
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

    // ── Branch / Loop compilers (stubs for valid graph support) ─────────

    private Slot<Dictionary<string, object?>, object?> CompileBranch(
        BranchNode node, Dictionary<string, object?> inputs,
        GraphConfig config, Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming, Dictionary<string, List<Edge>> outgoing)
    {
        // Default: pass through input
        var value = inputs.Values.FirstOrDefault();
        return Slot.Pure<Dictionary<string, object?>, object?>(value);
    }

    private Slot<Dictionary<string, object?>, object?> CompileLoop(
        LoopNode node, GraphConfig config,
        Dictionary<string, Node> nodeMap,
        Dictionary<string, List<Edge>> incoming, Dictionary<string, List<Edge>> outgoing)
    {
        // Default: return empty wins
        return Slot.Pure<Dictionary<string, object?>, object?>(Array.Empty<Win>());
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
