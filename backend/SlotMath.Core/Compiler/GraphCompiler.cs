using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Plugins;
using SlotMath.Core.Random;

namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  GraphCompiler — compiles a visual graph to a runnable Slot program (G14)
//
//  Compilation is EAGER and happens exactly once per Compile() call:
//    - Every node is compiled to a "chain" — a Func<object?, Slot<S, object?>>
//      taking the runtime input value and producing the program from that
//      node to the sink.  Chains are memoised per (nodeId, sinkId), so a
//      node shared by several paths compiles once.
//    - All expression compilation, registry/plugin resolution, weight-set
//      and reel-table construction happen at compile time.  At run time the
//      interpreters only thread values — nothing is re-compiled per spin.
//    - Nodes whose program does not depend on the runtime input (Draw, Loop)
//      are compiled to a single shared Slot instance.  Stable node identity
//      is what lets the exact interpreter memoise across loop iterations.
//
//  Semantics:
//    - Branch forks route to true/false output ports.
//    - Loop nodes use the Slot.Loop fixpoint with body/exit ports.
//    - State-op nodes (Get/Put/Modify) are side effects: the data-flow value
//      passes through unchanged.  State updates are copy-on-write — the
//      kernel state is never mutated in place (exact-path branches share
//      state references, and sampled spins share the initial state).
//    - Fan-out (several outgoing edges from a regular node) evaluates every
//      downstream path with the same input and sums the resulting amounts.
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
        // Phase 0: Inline subgraph (LibraryNode) references to their atoms.
        // After this the graph contains only primitives, which the validator
        // and builder already handle in full.
        var (inlined, inlineErrors) = SubgraphInliner.Inline(config);
        if (inlineErrors.Count > 0)
            return CompileResult.Failure(inlineErrors.ToList());
        config = inlined;

        // Phase 1: Validate
        var errors = GraphValidator.Validate(config, _pluginHost);
        if (errors.Count > 0)
            return CompileResult.Failure(errors);

        // Phase 2: Compile
        try
        {
            var builder = new ProgramBuilder(config, _pluginHost);
            var program = builder.Build();
            if (config.Plugins is { Length: > 0 })
                program = Slot.Annotate(program, "compiled-graph", containsPlugin: true);

            return CompileResult.Success(program) with { WinScale = builder.WinScale };
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

    // ════════════════════════════════════════════════════════════════════
    //  ProgramBuilder — one compilation pass over a validated graph
    // ════════════════════════════════════════════════════════════════════

    private sealed class ProgramBuilder
    {
        // The compiler's state shape: a string-keyed dictionary.
        // Aliased locally to keep signatures readable.
        private readonly GraphConfig _config;
        private readonly PluginHost? _pluginHost;
        private readonly Dictionary<string, Node> _nodeMap;
        private readonly Dictionary<string, List<Edge>> _incoming;
        private readonly Dictionary<string, List<Edge>> _outgoing;
        private readonly Dictionary<(string NodeId, string? SinkId), Func<object?, Slot<Dictionary<string, object?>, object?>>> _chains = new();
        private readonly HashSet<(string NodeId, string? SinkId)> _compiling = new();

        private readonly BigInteger _winScale;
        private readonly decimal _winScaleDecimal;

        public ProgramBuilder(GraphConfig config, PluginHost? pluginHost)
        {
            _config = config;
            _pluginHost = pluginHost;
            _nodeMap = config.Nodes.ToDictionary(n => n.Id);
            _incoming = BuildEdgeMap(config, e => e.TargetNodeId);
            _outgoing = BuildEdgeMap(config, e => e.SourceNodeId);
            _winScale = DetectWinScale(config);
            _winScaleDecimal = (decimal)_winScale;
        }

        /// <summary>Sub-credit scale applied to all win sources (1 = none).</summary>
        public BigInteger WinScale => _winScale;

        /// <summary>
        /// Determine the win scale: 10^(max decimal places over all paytable
        /// payouts).  Fractional payouts like "2.5" used to be silently
        /// truncated at the BigInteger boundary; scaling keeps them exact.
        /// </summary>
        private static BigInteger DetectWinScale(GraphConfig config)
        {
            var maxDecimals = 0;
            foreach (var paytable in config.Paytables ?? Array.Empty<Paytable>())
            {
                foreach (var entry in paytable.Entries)
                {
                    foreach (var payout in entry.Payouts)
                    {
                        if (!decimal.TryParse(payout,
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var value))
                        {
                            continue; // evaluators report unparseable payouts themselves
                        }

                        var decimals = DecimalPlaces(value);
                        if (decimals > 9)
                            throw new CompilationException(null, ErrorCodes.InvalidGraph,
                                $"Paytable payout '{payout}' has more than 9 decimal places.");
                        if (decimals > maxDecimals)
                            maxDecimals = decimals;
                    }
                }
            }

            return BigInteger.Pow(10, maxDecimals);
        }

        private static int DecimalPlaces(decimal value)
        {
            value = System.Math.Abs(value);
            var places = 0;
            while (value != decimal.Truncate(value) && places < 10)
            {
                value *= 10m;
                places++;
            }
            return places;
        }

        private static Dictionary<string, List<Edge>> BuildEdgeMap(
            GraphConfig config, Func<Edge, string> keySelector)
        {
            var map = new Dictionary<string, List<Edge>>();
            foreach (var node in config.Nodes)
                map[node.Id] = new List<Edge>();
            foreach (var edge in config.Edges)
            {
                if (map.TryGetValue(keySelector(edge), out var list))
                    list.Add(edge);
            }
            return map;
        }

        // ── Top level ───────────────────────────────────────────────────

        public Slot<Dictionary<string, object?>, BigInteger> Build()
        {
            var entryNodeIds = _config.Nodes
                .Where(n => _incoming[n.Id].Count == 0)
                .Select(n => n.Id)
                .ToList();

            if (entryNodeIds.Count == 0)
                throw new CompilationException(null, ErrorCodes.InvalidGraph, "Graph has no entry nodes.");

            var sinkNode = _config.Nodes.OfType<MetricsSinkNode>().First();
            var sinkId = sinkNode.Id;

            // Sink reads the win from a named state key: run the whole program
            // for its state effects, then read state[key].  This is the pure
            // atom + expression win path (no Win[]-producing molecule).
            if (sinkNode.WinStateKey is { } winKey)
            {
                Slot<Dictionary<string, object?>, object?> program =
                    Slot.Pure<Dictionary<string, object?>, object?>((object?)BigInteger.Zero);
                foreach (var entryId in entryNodeIds)
                {
                    var entryChain = GetChain(entryId, sinkId);
                    var captured = program;
                    program = captured.SelectMany(_ => entryChain(null));
                }

                return program.SelectMany(_ =>
                    Slot.GetState<Dictionary<string, object?>>().SelectMany(state =>
                        Slot.Pure<Dictionary<string, object?>, BigInteger>(
                            ReadStateWin(state, winKey))));
            }

            if (entryNodeIds.Count == 1)
            {
                var chain = GetChain(entryNodeIds[0], sinkId);
                return chain(null).SelectMany(v =>
                    Slot.Pure<Dictionary<string, object?>, BigInteger>(ExtractBigInteger(v)));
            }

            // Multiple entry nodes: compose all programs sequentially and sum.
            Slot<Dictionary<string, object?>, BigInteger> composed =
                Slot.Pure<Dictionary<string, object?>, BigInteger>(BigInteger.Zero);
            foreach (var entryId in entryNodeIds)
            {
                var chain = GetChain(entryId, sinkId);
                var capturedComposed = composed;
                composed = capturedComposed.SelectMany(acc =>
                    chain(null).SelectMany(v =>
                        Slot.Pure<Dictionary<string, object?>, BigInteger>(acc + ExtractBigInteger(v))));
            }

            return composed;
        }

        /// <summary>
        /// Read a win amount from the recurrence state and scale it to
        /// sub-credit units (consistent with Draw/paytable win sources).
        /// </summary>
        private BigInteger ReadStateWin(Dictionary<string, object?> state, string key)
        {
            if (!state.TryGetValue(key, out var raw))
                return BigInteger.Zero;
            var amount = raw switch
            {
                BigInteger bi => bi,
                int i => i,
                long l => l,
                _ => BigInteger.Zero,
            };
            return amount * _winScale;
        }

        // ── Chain compiler ──────────────────────────────────────────────

        /// <summary>
        /// Get (or compile) the chain for the subgraph starting at
        /// <paramref name="nodeId"/> and ending at <paramref name="sinkId"/>
        /// (null for loop bodies, which end at dead ends).
        /// </summary>
        private Func<object?, Slot<Dictionary<string, object?>, object?>> GetChain(
            string nodeId, string? sinkId)
        {
            var key = (nodeId, sinkId);
            if (_chains.TryGetValue(key, out var cached))
                return cached;

            if (!_compiling.Add(key))
                throw new CompilationException(nodeId, ErrorCodes.InvalidGraph,
                    $"Cyclic data flow through node '{nodeId}' — cycles are only supported through Loop body ports.");

            try
            {
                var chain = CompileChain(nodeId, sinkId);
                _chains[key] = chain;
                return chain;
            }
            finally
            {
                _compiling.Remove(key);
            }
        }

        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileChain(
            string nodeId, string? sinkId)
        {
            // Base case: reached the sink — the chain returns its input.
            if (sinkId != null && nodeId == sinkId)
                return static v => Slot.Pure<Dictionary<string, object?>, object?>(v);

            var node = _nodeMap[nodeId];
            var outgoingEdges = _outgoing[nodeId];

            // Special: Branch with true/false output ports
            if (node is BranchNode branch)
            {
                var trueEdge = outgoingEdges.FirstOrDefault(e => e.SourcePort == "true");
                var falseEdge = outgoingEdges.FirstOrDefault(e => e.SourcePort == "false");
                if (trueEdge != null || falseEdge != null)
                    return CompileBranchFork(branch, sinkId, trueEdge, falseEdge);
            }

            // Special: Loop with body/exit output ports
            if (node is LoopNode loop)
            {
                var bodyEdge = outgoingEdges.FirstOrDefault(e => e.SourcePort == "body");
                if (bodyEdge != null)
                {
                    var exitEdge = outgoingEdges.FirstOrDefault(e =>
                        e.SourcePort == "exit" || e.SourcePort == "out");
                    return CompileLoopChain(loop, sinkId, bodyEdge, exitEdge);
                }
            }

            // Regular node: compile its output, then follow outgoing edges.
            var nodeOutput = CompileNodeOutput(node);

            if (outgoingEdges.Count == 0)
                return nodeOutput; // dead end (loop body terminal)

            // State operations are side effects: the data-flow value passes
            // through unchanged.
            var passThroughInput = node is GetStateNode or PutStateNode or ModifyStateNode;

            if (outgoingEdges.Count == 1)
            {
                var next = GetChain(outgoingEdges[0].TargetNodeId, sinkId);

                if (passThroughInput)
                    return v => nodeOutput(v).SelectMany(_ => next(v));

                // Input-independent nodes compile to one shared program spine —
                // a single FlatMap whose identity is stable across all spins
                // and loop iterations (this is what the exact interpreter's
                // memoisation keys on).
                if (IsInputIndependent(node))
                {
                    var spine = nodeOutput(null).SelectMany(next);
                    return _ => spine;
                }

                return v => nodeOutput(v).SelectMany(next);
            }

            // Fan-out: evaluate every downstream path with the same input and
            // sum the resulting amounts.
            var nextChains = outgoingEdges
                .Select(e => GetChain(e.TargetNodeId, sinkId))
                .ToArray();

            Func<object?, Slot<Dictionary<string, object?>, object?>> fanOut = v =>
            {
                Slot<Dictionary<string, object?>, object?> sum =
                    Slot.Pure<Dictionary<string, object?>, object?>(BigInteger.Zero);
                foreach (var chain in nextChains)
                {
                    var capturedSum = sum;
                    sum = capturedSum.SelectMany(acc =>
                        chain(v).SelectMany(result =>
                            Slot.Pure<Dictionary<string, object?>, object?>(
                                ExtractBigInteger(acc) + ExtractBigInteger(result))));
                }
                return sum;
            };

            if (passThroughInput)
                return v => nodeOutput(v).SelectMany(_ => fanOut(v));
            if (IsInputIndependent(node))
            {
                var spine = nodeOutput(null).SelectMany(v => fanOut(v));
                return _ => spine;
            }
            return v => nodeOutput(v).SelectMany(fanOut);
        }

        /// <summary>
        /// True when the node's compiled program ignores its runtime input,
        /// allowing the chain to be a single shared Slot instance.
        /// (State-op nodes never reach this check — they take the
        /// pass-through path.)
        /// </summary>
        private static bool IsInputIndependent(Node node) =>
            node is DrawNode or LoopNode;

        // ── Branch fork ─────────────────────────────────────────────────

        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileBranchFork(
            BranchNode node, string? sinkId, Edge? trueEdge, Edge? falseEdge)
        {
            Func<Board?, Dictionary<string, object?>, bool>? condition = null;
            if (node.ConditionId != null
                && _config.Expressions != null
                && _config.Expressions.TryGetValue(node.ConditionId, out var condExpr))
            {
                condition = ExpressionCompiler.CompileBoolean(condExpr);
            }

            var trueChain = trueEdge != null ? GetChain(trueEdge.TargetNodeId, sinkId) : null;
            var falseChain = falseEdge != null ? GetChain(falseEdge.TargetNodeId, sinkId) : null;

            return v => Slot.GetState<Dictionary<string, object?>>()
                .SelectMany(state =>
                {
                    var pass = condition?.Invoke(v as Board, state) ?? false;

                    if (pass && trueChain != null)
                        return trueChain(v);
                    if (!pass && falseChain != null)
                        return falseChain(v);

                    return Slot.Pure<Dictionary<string, object?>, object?>((object?)BigInteger.Zero);
                });
        }

        // ── Loop (proper Slot.Loop fixpoint) ────────────────────────────

        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileLoopChain(
            LoopNode node, string? sinkId, Edge bodyEdge, Edge? exitEdge)
        {
            var iterKey = $"__iter_{node.Id}__";
            var winsKey = $"__wins_{node.Id}__";
            var maxIter = node.MaxIterations > 0 && node.MaxIterations <= 10000
                ? node.MaxIterations : 100;

            // Stop condition: MaxIterations safety cap + optional expression
            Func<Dictionary<string, object?>, bool> stopFn = s =>
            {
                var iter = s.TryGetValue(iterKey, out var v) && v is int i ? i : 0;
                return iter >= maxIter;
            };

            if (node.StopConditionId != null
                && _config.Expressions != null
                && _config.Expressions.TryGetValue(node.StopConditionId, out var stopExpr))
            {
                var compiledStop = ExpressionCompiler.CompileBoolean(stopExpr);
                var prevStop = stopFn;
                stopFn = s => prevStop(s) || compiledStop(null, s);
            }

            // Body program: body subgraph (compiled once) + accumulate result
            // + increment counter.  Copy-on-write: the previous state object
            // is never mutated.
            var bodySlot = GetChain(bodyEdge.TargetNodeId, sinkId: null)(null);
            var body = bodySlot.SelectMany(result =>
                Slot.Modify<Dictionary<string, object?>>(s =>
                {
                    var next = new Dictionary<string, object?>(s);
                    var iter = s.TryGetValue(iterKey, out var v) && v is int i ? i : 0;
                    next[iterKey] = iter + 1;
                    if (result is BigInteger val)
                    {
                        var acc = s.TryGetValue(winsKey, out var w) && w is BigInteger ew
                            ? ew : BigInteger.Zero;
                        next[winsKey] = acc + val;
                    }
                    else if (result is Win[] wins)
                    {
                        var acc = s.TryGetValue(winsKey, out var w) && w is BigInteger ew
                            ? ew : BigInteger.Zero;
                        next[winsKey] = acc + SumWins(wins);
                    }

                    return next;
                }));

            var readWins = Slot.GetState<Dictionary<string, object?>, object?>(s =>
                s.TryGetValue(winsKey, out var w) ? w : (object?)BigInteger.Zero);

            // Initialise state, run the loop, read the accumulated wins —
            // all input-independent, so the whole loop program is one shared
            // Slot instance.
            var loopProgram = Slot.Modify<Dictionary<string, object?>>(s =>
                {
                    var next = new Dictionary<string, object?>(s);
                    next[iterKey] = 0;
                    next[winsKey] = BigInteger.Zero;
                    return next;
                })
                .SelectMany(_ => Slot.Loop(stopFn, body))
                .SelectMany(_ => readWins);

            if (exitEdge != null)
            {
                var exitChain = GetChain(exitEdge.TargetNodeId, sinkId);
                var withExit = loopProgram.SelectMany(exitChain);
                return _ => withExit;
            }

            return _ => loopProgram;
        }

        // ── Node output dispatch ────────────────────────────────────────

        /// <summary>
        /// Compile a single node's output: a function from the runtime input
        /// value to a Slot producing the node's primary output value.  All
        /// expression/registry/table resolution happens here, once.
        /// </summary>
        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileNodeOutput(Node node)
        {
            switch (node)
            {
                case DrawNode d:
                    var drawSlot = CompileDraw(d);
                    return _ => drawSlot;
                case MapNode m:
                    return CompileMap(m);
                case GetStateNode gs:
                    var getSlot = CompileGetState(gs);
                    return _ => getSlot;
                case PutStateNode ps:
                    return CompilePutState(ps);
                case ModifyStateNode ms:
                    var modifySlot = CompileModifyState(ms);
                    return _ => modifySlot;
                case BranchNode b:
                    return CompileBranchFallback(b);
                case LoopNode l:
                    var legacySlot = CompileLegacyLoop(l);
                    return _ => legacySlot;
                case MetricsSinkNode:
                    return static v => Slot.Pure<Dictionary<string, object?>, object?>(v);
                default:
                    return static _ => Slot.Pure<Dictionary<string, object?>, object?>(null);
            }
        }

        // ── Draw ────────────────────────────────────────────────────────

        private Slot<Dictionary<string, object?>, object?> CompileDraw(DrawNode drawNode)
        {
            // Level (b): expression-driven weights + inline outcome values
            if (drawNode.WeightExpressionId != null
                && _config.Expressions != null
                && _config.Expressions.TryGetValue(drawNode.WeightExpressionId, out var weightExpr)
                && drawNode.DrawWeights is { Length: > 0 })
            {
                var compiledWeights = ExpressionCompiler.CompileWeights(weightExpr);
                return BuildDrawSlot(
                    state => compiledWeights(state), drawNode.DrawWeights, drawNode.StateWriteKey);
            }

            // Level (a): inline weighted draw (custom outcomes, no ReelSets required)
            if (drawNode.DrawWeights is { Length: > 0 })
            {
                var dw = drawNode.DrawWeights;
                var inlineWeights = WeightSet.FromIntegers(Array.ConvertAll(dw, w => w.Weight));
                return BuildDrawSlot(_ => inlineWeights, dw, drawNode.StateWriteKey);
            }

            // Level (a): reel-strip draw (slot machine)
            var reelSet = _config.ReelSets.FirstOrDefault()
                ?? throw new CompilationException(drawNode.Id, ErrorCodes.InvalidGraph,
                    "No ReelSet defined in graph config. Add reel strips or configure inline draw weights.");

            var strips = reelSet.StripIds
                .Select(sid => _config.ReelStrips.FirstOrDefault(s => s.Id == sid))
                .Where(s => s != null)
                .Select(s => s!)
                .ToArray();

            if (strips.Length == 0)
                throw new CompilationException(drawNode.Id, ErrorCodes.InvalidGraph,
                    $"ReelSet '{reelSet.Id}' references no valid reel strips.");

            var rows = _config.BoardConfig?.Rows ?? 3;
            var weights = BuildReelWeights(strips, drawNode.Id);

            return Slot.Draw<Dictionary<string, object?>, object?>(
                _ => weights,
                choiceIndex => BuildBoardFromChoice(choiceIndex, strips, rows));
        }

        private Slot<Dictionary<string, object?>, object?> BuildDrawSlot(
            Func<Dictionary<string, object?>, WeightSet> weights,
            DrawWeight[] dw,
            string? stateWriteKey)
        {
            // Outcome programs are pre-built once per compile, so taking a
            // branch at run time allocates nothing.  Values are emitted in
            // sub-credit units (multiplied by the win scale).
            var outcomes = new Slot<Dictionary<string, object?>, object?>[dw.Length];

            if (stateWriteKey == null)
            {
                for (var i = 0; i < dw.Length; i++)
                {
                    outcomes[i] = Slot.Pure<Dictionary<string, object?>, object?>(
                        (object?)(new BigInteger(dw[i].Value) * _winScale));
                }
                return Slot.DrawFrom(weights, outcomes);
            }

            // Record the drawn outcome id under the user-defined state key.
            // Copy-on-write: branches of the exact interpreter share the
            // pre-draw state object, so it must never be mutated in place.
            for (var i = 0; i < dw.Length; i++)
            {
                var outcomeId = dw[i].OutcomeId;
                var value = Slot.Pure<Dictionary<string, object?>, object?>(
                    (object?)(new BigInteger(dw[i].Value) * _winScale));
                outcomes[i] = new ModifyState<Dictionary<string, object?>, object?>(
                    s =>
                    {
                        var next = new Dictionary<string, object?>(s);
                        next[stateWriteKey] = outcomeId;
                        return next;
                    },
                    value);
            }
            return Slot.DrawFrom(weights, outcomes);
        }

        private static WeightSet BuildReelWeights(ReelStrip[] strips, string nodeId)
        {
            // Each outcome is an index into the product space of reel stop
            // positions, drawn uniformly.
            long totalOutcomes = 1;
            foreach (var strip in strips)
            {
                totalOutcomes *= strip.Symbols.Length;
                if (totalOutcomes > 1_000_000)
                    throw new CompilationException(nodeId, ErrorCodes.InvalidGraph,
                        "Too many reel combinations (more than 1,000,000) for a single draw.");
            }

            return WeightSet.Uniform((int)totalOutcomes);
        }

        private static Board BuildBoardFromChoice(int choiceIndex, ReelStrip[] strips, int rows)
        {
            int cols = strips.Length;
            var cells = new BoardCell[rows, cols];

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
                    cells[r, c] = new BoardCell { Symbols = new[] { symbolId } };
                }
            }

            return Board.FromCells(cells);
        }

        // ── Map (evaluator / transform) ─────────────────────────────────

        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileMap(MapNode mapNode)
        {
            // Pre-compile expression-valued input ports.  A port named "in"
            // is occupied at runtime when an input value arrives, so its
            // default only applies when the input is null.
            var expressionPorts = new List<(string PortName, Func<Board?, object?, BigInteger> Compiled)>();
            foreach (var (portName, port) in mapNode.Inputs)
            {
                if (port.DefaultValue != null)
                    expressionPorts.Add((portName, ExpressionCompiler.CompileNumber(port.DefaultValue)));
            }

            Func<Board?, Dictionary<string, object?>, Dictionary<string, BigInteger>?,
                (object? Result, Dictionary<string, object?> NewState)>? applyTransform =
                mapNode.TransformId != null
                    ? ResolveTransform(mapNode.TransformId, mapNode.Id)
                    : null;

            var hasBoardInput = mapNode.Inputs.Values.Any(p => p.Type == PortType.Board);
            var hasWinsOutput = mapNode.Outputs.Values.Any(p => p.Type == PortType.Wins);

            return v =>
            {
                var board = v as Board;

                Dictionary<string, BigInteger>? expressionValues = null;
                if (expressionPorts.Count > 0)
                {
                    var inputs = v != null
                        ? new Dictionary<string, object?> { ["in"] = v }
                        : new Dictionary<string, object?>();
                    foreach (var (portName, compiled) in expressionPorts)
                    {
                        if (!inputs.ContainsKey(portName))
                        {
                            expressionValues ??= new Dictionary<string, BigInteger>();
                            expressionValues[portName] = compiled(board, inputs);
                        }
                    }
                }

                if (applyTransform != null)
                    return Slot.GetState<Dictionary<string, object?>>()
                        .SelectMany(currentState =>
                        {
                            var (result, newState) = applyTransform(board, currentState, expressionValues);
                            var resultSlot = Slot.Pure<Dictionary<string, object?>, object?>(result);
                            return ReferenceEquals(newState, currentState)
                                ? resultSlot
                                : Slot.Modify<Dictionary<string, object?>>(_ => newState)
                                    .SelectMany(_ => resultSlot);
                        });

                if (hasBoardInput && hasWinsOutput && board != null)
                {
                    // Board → Wins with no evaluator specified: empty wins.
                    return Slot.Pure<Dictionary<string, object?>, object?>(Array.Empty<Win>());
                }

                // Pass through the board if nothing else matches.
                return Slot.Pure<Dictionary<string, object?>, object?>(board!);
            };
        }

        /// <summary>
        /// Resolve a transform/evaluator/plugin reference at compile time to
        /// a state-aware runtime application function.
        ///
        /// The returned delegate receives the current game state and returns
        /// both the result and the (possibly updated) game state.  Evaluators
        /// return state unchanged; transforms may return a new state object.
        /// </summary>
        private Func<Board?, Dictionary<string, object?>, Dictionary<string, BigInteger>?,
            (object? Result, Dictionary<string, object?> NewState)> ResolveTransform(
            string transformId, string nodeId)
        {
            // Plugin reference: "plugin:pluginId"
            if (transformId.StartsWith("plugin:"))
            {
                var pluginId = transformId["plugin:".Length..];

                // ITransform plugin — may read and write state
                var pluginTransform = _pluginHost?.TryGetTransform(pluginId);
                if (pluginTransform != null)
                {
                    return (board, state, _) =>
                    {
                        var (newBoard, newStateObj) = pluginTransform.Apply(board!, state);
                        var newState = newStateObj as Dictionary<string, object?> ?? state;
                        return (newBoard, newState);
                    };
                }

                // IEvaluator plugin — reads state, does not modify it
                var pluginEvaluator = _pluginHost?.TryGetEvaluator(pluginId);
                if (pluginEvaluator == null)
                    throw new CompilationException(nodeId, ErrorCodes.PluginNotFound,
                        $"Plugin '{pluginId}' not found.");

                return (board, state, expressionValues) => (
                    ApplyExpressions(pluginEvaluator.Evaluate(board!, state), expressionValues),
                    state
                );
            }

            // Evaluator registry — evaluators read state but never modify it
            var registryEvaluator = EvaluatorRegistry.TryGet(transformId);
            if (registryEvaluator != null)
            {
                return (board, state, expressionValues) => (
                    ApplyExpressions(registryEvaluator.Evaluate(board!, state), expressionValues),
                    state
                );
            }

            // Transform registry — transforms may read and write state
            var transform = TransformRegistry.TryGet(transformId);
            if (transform != null)
            {
                return (board, state, _) =>
                {
                    var (newBoard, newStateObj) = transform.Apply(board!, state);
                    var newState = newStateObj as Dictionary<string, object?> ?? state;
                    return (newBoard, newState);
                };
            }

            throw new CompilationException(nodeId, ErrorCodes.MissingTransform,
                $"Transform/evaluator '{transformId}' not found.");
        }

        private static Win[] ApplyExpressions(Win[] wins, Dictionary<string, BigInteger>? expressionValues)
        {
            if (expressionValues is null || expressionValues.Count == 0) return wins;

            var multiplier = BigInteger.One;
            foreach (var v in expressionValues.Values)
                multiplier *= v;
            if (multiplier == BigInteger.One) return wins;

            return Array.ConvertAll(wins, w => new Win
            {
                SymbolId = w.SymbolId,
                Count = w.Count,
                Positions = w.Positions,
                Payout = w.Payout,
                Multiplier = w.Multiplier * (decimal)multiplier,
                EvaluatorName = w.EvaluatorName,
            });
        }

        // ── State nodes ─────────────────────────────────────────────────

        private static Slot<Dictionary<string, object?>, object?> CompileGetState(GetStateNode node)
        {
            var key = node.StateKey ?? "__default__";
            return Slot.GetState<Dictionary<string, object?>, object?>(state =>
            {
                state.TryGetValue(key, out var value);
                return value;
            });
        }

        private static Func<object?, Slot<Dictionary<string, object?>, object?>> CompilePutState(
            PutStateNode node)
        {
            var key = node.StateKey;
            // Copy-on-write: never mutate the incoming state object — it is
            // shared across exact-path branches and across sampled spins.
            return v => Slot.Modify<Dictionary<string, object?>>(state =>
                {
                    var next = new Dictionary<string, object?>(state);
                    next[key] = v;
                    return next;
                })
                .SelectMany(_ => Slot.Pure<Dictionary<string, object?>, object?>(v));
        }

        private Slot<Dictionary<string, object?>, object?> CompileModifyState(ModifyStateNode node)
        {
            Expression? expr = null;
            if (node.ExpressionId != null && _config.Expressions != null)
                _config.Expressions.TryGetValue(node.ExpressionId, out expr);

            // Modify state as a side effect, return null (caller passes through
            // the previous output).  Copy-on-write when writing — the incoming
            // state object is shared across exact-path branches and spins.
            if (expr is null)
                return Slot.Modify<Dictionary<string, object?>>(static state => state)
                    .SelectMany(static _ => Slot.Pure<Dictionary<string, object?>, object?>(null!));

            // Atom path: write the expression's TYPED result to a named key.
            // This is what lets a graph compute a value over state (a fold
            // producing a win symbol, a conditional producing a payout) with no
            // evaluator/transform molecule.
            if (node.OutputKey is { } outputKey)
            {
                var capturedExpr = expr;
                return Slot.Modify<Dictionary<string, object?>>(state =>
                    {
                        var next = new Dictionary<string, object?>(state);
                        next[outputKey] = EvaluateToStateObject(capturedExpr, state);
                        return next;
                    })
                    .SelectMany(static _ => Slot.Pure<Dictionary<string, object?>, object?>(null!));
            }

            // Legacy path: numeric result written to the internal "__modified__".
            var compiled = ExpressionCompiler.CompileNumber(expr);
            return Slot.Modify<Dictionary<string, object?>>(state =>
                {
                    var next = new Dictionary<string, object?>(state);
                    next["__modified__"] = compiled(null, state);
                    return next;
                })
                .SelectMany(static _ => Slot.Pure<Dictionary<string, object?>, object?>(null!));
        }

        /// <summary>
        /// Evaluate an expression over the current state to its natural CLR
        /// value for storage in the state dictionary: BigInteger for amounts,
        /// string for symbols, bool for predicates.  Exact (rational) path.
        /// </summary>
        private static object? EvaluateToStateObject(
            Expression expr, Dictionary<string, object?> state)
        {
            var v = ExactExpressionEvaluator.Evaluate(expr, new EvalContext { State = state });
            return v.ToStateObject();
        }

        // ── Branch fallback (no true/false ports) ───────────────────────

        private Func<object?, Slot<Dictionary<string, object?>, object?>> CompileBranchFallback(
            BranchNode node)
        {
            if (node.ConditionId != null
                && _config.Expressions != null
                && _config.Expressions.TryGetValue(node.ConditionId, out var condExpr))
            {
                var compiledCond = ExpressionCompiler.CompileBoolean(condExpr);
                return v => Slot.GetState<Dictionary<string, object?>>()
                    .SelectMany(state =>
                    {
                        var pass = compiledCond(v as Board, state);
                        return Slot.Pure<Dictionary<string, object?>, object?>(
                            pass ? v : (object?)BigInteger.Zero);
                    });
            }

            return static v => Slot.Pure<Dictionary<string, object?>, object?>(v);
        }

        // ── Legacy loop (no body port — backward compat) ────────────────

        private Slot<Dictionary<string, object?>, object?> CompileLegacyLoop(LoopNode node)
        {
            int maxIter = node.MaxIterations > 0 && node.MaxIterations <= 500
                ? node.MaxIterations : 5;

            var iterKey = $"__legacyiter_{node.Id}__";
            Func<Dictionary<string, object?>, bool> stopFn = s =>
            {
                var iter = s.TryGetValue(iterKey, out var v) && v is int i ? i : 0;
                return iter >= maxIter;
            };

            if (node.StopConditionId != null
                && _config.Expressions != null
                && _config.Expressions.TryGetValue(node.StopConditionId, out var stopExpr))
            {
                var compiledStop = ExpressionCompiler.CompileBoolean(stopExpr);
                var prevStop = stopFn;
                stopFn = s => prevStop(s) || compiledStop(null, s);
            }

            // Find the body node from incoming edges
            var bodyEdges = _incoming[node.Id];
            if (bodyEdges.Count == 0)
                return Slot.Pure<Dictionary<string, object?>, object?>((object?)BigInteger.Zero);

            var bodyNode = _nodeMap[bodyEdges[0].SourceNodeId];
            var winsKey = $"__legacywins_{node.Id}__";

            var bodyOutput = CompileNodeOutput(bodyNode);
            var body = bodyOutput(null).SelectMany(result =>
                Slot.Modify<Dictionary<string, object?>>(s =>
                {
                    var next = new Dictionary<string, object?>(s);
                    var iter = s.TryGetValue(iterKey, out var v) && v is int i ? i : 0;
                    next[iterKey] = iter + 1;
                    var val = result is BigInteger bi ? bi : BigInteger.Zero;
                    var acc = s.TryGetValue(winsKey, out var w) && w is BigInteger ew
                        ? ew : BigInteger.Zero;
                    next[winsKey] = acc + val;
                    return next;
                }));

            return Slot.Modify<Dictionary<string, object?>>(s =>
                {
                    var next = new Dictionary<string, object?>(s);
                    next[iterKey] = 0;
                    next[winsKey] = BigInteger.Zero;
                    return next;
                })
                .SelectMany(_ => Slot.Loop(stopFn, body))
                .SelectMany(_ =>
                    Slot.GetState<Dictionary<string, object?>, object?>(s =>
                        s.TryGetValue(winsKey, out var w) ? w : (object?)BigInteger.Zero));
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private BigInteger ExtractBigInteger(object? value)
        {
            switch (value)
            {
                case BigInteger bi:
                    // Already in sub-credit units (scaled at its source).
                    return bi;
                case Win[] wins:
                    return SumWins(wins);
                case decimal d:
                    return ToScaledWin(d);
                default:
                    return BigInteger.Zero;
            }
        }

        private BigInteger SumWins(Win[] wins)
        {
            var total = 0m;
            foreach (var w in wins)
                total += w.TotalWin;
            return ToScaledWin(total);
        }

        /// <summary>
        /// Convert a credit amount (exact decimal) to sub-credit integer
        /// units.  Paytable-derived totals are integral after scaling by
        /// construction; anything else (e.g. plugin multipliers, which are
        /// sampled-regime anyway) rounds half-to-even instead of silently
        /// truncating.
        /// </summary>
        private BigInteger ToScaledWin(decimal credits)
        {
            var scaled = credits * _winScaleDecimal;
            return new BigInteger(decimal.Round(scaled, 0, MidpointRounding.ToEven));
        }
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
