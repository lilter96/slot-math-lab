using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Compiler;

using SlotMathConstants = SlotMath.Core.SlotMathConstants;

// ═══════════════════════════════════════════════════════════════════════════
//  GraphValidator — validates a graph config before compilation
//
//  Checks:
//    1. Exactly one MetricsSink
//    2. No cycles except through Loop nodes
//    3. Edge type compatibility
//    4. Expression type-checking
//    5. Plugin existence and conformance
//    6. Reachability (all nodes reachable from entry and reach sink)
// ═══════════════════════════════════════════════════════════════════════════

public static class GraphValidator
{
    /// <summary>
    /// Validate a graph config. Returns empty list if valid.
    /// </summary>
    public static IReadOnlyList<CompileError> Validate(
        GraphConfig config,
        PluginHost? pluginHost,
        TypeCheckContext? typeCheckContext = null)
    {
        var errors = new List<CompileError>();
        try
        {
            EvidenceInput.Validate(config.EvidenceInputs ?? []);
            foreach (var sink in config.Nodes.OfType<MetricsSinkNode>()) sink.Settlement?.Validate();
            if (config.Nodes.OfType<MetricsSinkNode>().Any(s => s.Settlement is not null)
                && (config.InitialState?.Keys.Any(MonetarySettlement.EvidenceKeys.Contains) == true
                    || config.Nodes.OfType<ModifyStateNode>().Any(n => MonetarySettlement.EvidenceKeys.Contains(n.OutputKey))))
                throw new ArgumentException("Settlement evidence fields are reserved and cannot be authored.");
        }
        catch (ArgumentException ex) { errors.Add(new CompileError { Code = ErrorCodes.InvalidGraph, Message = ex.Message }); }

        if (config.Nodes.Length == 0)
        {
            errors.Add(new CompileError
            {
                Code = ErrorCodes.InvalidGraph,
                Message = "Graph has no nodes.",
            });
            return errors;
        }

        // 1. MetricsSink count
        ValidateMetricsSink(config, errors);

        // 2. Edge type compatibility (catches non-existent nodes early)
        ValidateEdgeTypes(config, errors);

        // 3. Build adjacency and validate cycles / reachability
        var adjacency = BuildAdjacency(config);
        ValidateAcyclicityAndReachability(config, adjacency, errors);

        // Resolve named references before checking the expression they denote.
        // References are compile-time syntax, never permissive state paths.
        foreach (var (name, expression) in config.Expressions ?? new())
            if (!ExpressionCost.WithinBudget(expression, out var cost))
                errors.Add(new CompileError { Code = ErrorCodes.ExpressionBudgetExceeded,
                    Message = $"Expression '{name}' has static cost {cost}, exceeding {SlotMathConstants.Expression.MaxOps} operations." });
        if (errors.Any(e => e.Code == ErrorCodes.ExpressionBudgetExceeded)) return errors;
        try { config = ExpressionResolver.Resolve(config); }
        catch (CompilationException ex)
        {
            errors.Add(new CompileError { NodeId = ex.NodeId, Code = ex.ErrorCode, Message = ex.Message });
            return errors;
        }
        catch (System.Text.Json.JsonException)
        {
            errors.Add(new CompileError { Code = ErrorCodes.ExpressionTypeError,
                Message = "An expression exceeds the supported serialization depth or contains invalid reference data." });
            return errors;
        }
        // 4. Expression type-checking
        ValidateExpressions(config, typeCheckContext, errors);
        foreach (var loop in config.Nodes.OfType<LoopNode>().Where(l => l.ExitReason is not null))
        {
            if (ExpressionCost.Compute(loop.ExitReason!) > 1000) errors.Add(new CompileError { NodeId = loop.Id, Code = ErrorCodes.ExpressionBudgetExceeded, Message = "Loop exit classification exceeds its expression budget." });
            foreach (var error in ExpressionTypeChecker.Check(loop.ExitReason!, typeCheckContext ?? BuildExpressionTypeContext(config), ExprType.String)) errors.Add(new CompileError { NodeId = loop.Id, Code = ErrorCodes.ExpressionTypeError, Message = error.Message });
        }

        // 5. Plugin validation
        ValidatePlugins(config, pluginHost, errors);

        // 6. Loop iteration caps (D6, invariant 8)
        ValidateLoopCaps(config, errors);

        return errors;
    }

    // ── 6. Loop iteration caps (D6) ──────────────────────────────────────

    private static void ValidateLoopCaps(GraphConfig config, List<CompileError> errors)
    {
        foreach (var loop in config.Nodes.OfType<LoopNode>())
        {
            if (loop.MaxIterations < 1 || loop.MaxIterations > SlotMathConstants.Loop.CapMax)
            {
                errors.Add(new CompileError
                {
                    NodeId = loop.Id,
                    Code = ErrorCodes.OverMaxLoopCap,
                    Message =
                        $"Loop node '{loop.Id}' has iteration cap {loop.MaxIterations}, which must be in " +
                        $"[1, {SlotMathConstants.Loop.CapMax}] (D6). Every Loop carries a mandatory, bounded cap.",
                });
            }
        }
    }

    // ── Subgraph reference graph: cycles (D7/G14) + nesting depth (D7) ────

    /// <summary>
    /// Statically validate subgraph (LibraryNode → CustomMechanic) references
    /// BEFORE inlining: reject circular references (A → B → A) and references
    /// nested deeper than the D7 cap, each with a precise coded error (D20).
    /// </summary>
    public static IReadOnlyList<CompileError> ValidateSubgraphReferences(GraphConfig config)
    {
        var errors = new List<CompileError>();
        if (config.Mechanics is null || config.Mechanics.Count == 0)
            return errors;

        // mechanic name → the mechanic names its own LibraryNodes reference
        var refs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, mechanic) in config.Mechanics)
            refs[name] = mechanic.Nodes.OfType<LibraryNode>().Select(l => l.MechanicName).ToList();

        // ── Cycle detection (DFS colouring) over defined mechanics ──
        var color = new Dictionary<string, int>(StringComparer.Ordinal); // 0=white 1=gray 2=black
        string? cyclicMechanic = null;

        bool Dfs(string name)
        {
            if (!refs.TryGetValue(name, out var children))
                return false; // undefined mechanic — the inliner reports it
            color[name] = 1;
            foreach (var child in children)
            {
                var c = color.GetValueOrDefault(child, 0);
                if (c == 1) { cyclicMechanic = child; return true; }
                if (c == 0 && Dfs(child)) return true;
            }
            color[name] = 2;
            return false;
        }

        foreach (var name in refs.Keys)
        {
            if (color.GetValueOrDefault(name, 0) == 0 && Dfs(name))
                break;
        }

        if (cyclicMechanic is not null)
        {
            var offender = config.Nodes.OfType<LibraryNode>().FirstOrDefault()
                ?? config.Mechanics.Values
                    .SelectMany(m => m.Nodes.OfType<LibraryNode>())
                    .FirstOrDefault(l => l.MechanicName == cyclicMechanic);
            errors.Add(new CompileError
            {
                NodeId = offender?.Id,
                Code = ErrorCodes.CircularSubgraphReference,
                Message =
                    $"Circular subgraph reference detected involving mechanic '{cyclicMechanic}'. " +
                    "Subgraph references must form a DAG (D7).",
            });
            return errors; // depth analysis is meaningless once a cycle exists
        }

        // ── Nesting depth (longest reference chain) ──
        var depthMemo = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string name)
        {
            if (!refs.TryGetValue(name, out var children))
                return 0;
            if (depthMemo.TryGetValue(name, out var cached))
                return cached;
            var max = 0;
            foreach (var child in children)
                max = System.Math.Max(max, Depth(child));
            return depthMemo[name] = 1 + max;
        }

        foreach (var lib in config.Nodes.OfType<LibraryNode>())
        {
            if (!config.Mechanics.ContainsKey(lib.MechanicName))
                continue;
            var depth = Depth(lib.MechanicName);
            if (depth > SlotMathConstants.Budget.MaxSubgraphNestingDepth)
            {
                errors.Add(new CompileError
                {
                    NodeId = lib.Id,
                    Code = ErrorCodes.SubgraphNestingTooDeep,
                    Message =
                        $"Subgraph nesting depth {depth} at library node '{lib.Id}' exceeds the maximum " +
                        $"of {SlotMathConstants.Budget.MaxSubgraphNestingDepth} (D7).",
                });
            }
        }

        return errors;
    }

    // ── 1. MetricsSink ──────────────────────────────────────────────────

    private static void ValidateMetricsSink(GraphConfig config, List<CompileError> errors)
    {
        var sinks = config.Nodes.OfType<MetricsSinkNode>().ToArray();

        if (sinks.Length == 0)
        {
            errors.Add(new CompileError
            {
                Code = ErrorCodes.MissingMetricsSink,
                Message = "Graph must contain exactly one MetricsSink node, but none was found.",
            });
            return;
        }

        if (sinks.Length > 1)
        {
            foreach (var sink in sinks)
            {
                errors.Add(new CompileError
                {
                    NodeId = sink.Id,
                    Code = ErrorCodes.DuplicateMetricsSink,
                    Message = $"Duplicate MetricsSink node '{sink.Id}'. A graph must contain exactly one MetricsSink.",
                });
            }
            return;
        }

        // D6/D19: every game must declare a finite round win cap.
        var sole = sinks[0];
        if (sole.WinCap is null or <= 0)
        {
            errors.Add(new CompileError
            {
                NodeId = sole.Id,
                Code = ErrorCodes.MissingWinCap,
                Message = $"MetricsSink '{sole.Id}' must declare a positive WinCap (D6/D19). " +
                          "Every game requires a finite round win cap for exact-path safety.",
            });
        }
    }

    // ── 2. Cycles & reachability ────────────────────────────────────────

    private sealed record AdjacencyInfo(
        Dictionary<string, List<Edge>> Incoming,
        Dictionary<string, List<Edge>> Outgoing,
        HashSet<string> AllNodes)
    {
        public HashSet<string> EntryNodes { get; init; } = new();
    }

    private static AdjacencyInfo BuildAdjacency(GraphConfig config)
    {
        var incoming = new Dictionary<string, List<Edge>>();
        var outgoing = new Dictionary<string, List<Edge>>();
        var allNodes = new HashSet<string>();

        foreach (var node in config.Nodes)
        {
            allNodes.Add(node.Id);
            incoming[node.Id] = new List<Edge>();
            outgoing[node.Id] = new List<Edge>();
        }

        foreach (var edge in config.Edges)
        {
            // Add source and target to the node set even if they're not in config.Nodes
            // (they'll be flagged as invalid edges, but we need to not crash on them)
            if (!outgoing.ContainsKey(edge.SourceNodeId))
                outgoing[edge.SourceNodeId] = new List<Edge>();
            if (!outgoing.ContainsKey(edge.TargetNodeId))
                outgoing[edge.TargetNodeId] = new List<Edge>();
            if (!incoming.ContainsKey(edge.SourceNodeId))
                incoming[edge.SourceNodeId] = new List<Edge>();
            if (!incoming.ContainsKey(edge.TargetNodeId))
                incoming[edge.TargetNodeId] = new List<Edge>();
            if (!allNodes.Contains(edge.SourceNodeId))
                allNodes.Add(edge.SourceNodeId);
            if (!allNodes.Contains(edge.TargetNodeId))
                allNodes.Add(edge.TargetNodeId);

            outgoing[edge.SourceNodeId].Add(edge);
            incoming[edge.TargetNodeId].Add(edge);
        }

        var entryNodes = new HashSet<string>(allNodes.Where(n =>
            incoming.ContainsKey(n) && incoming[n].Count == 0));

        return new AdjacencyInfo(incoming, outgoing, allNodes) { EntryNodes = entryNodes };
    }

    private static void ValidateAcyclicityAndReachability(
        GraphConfig config, AdjacencyInfo adj, List<CompileError> errors)
    {
        var loopNodes = new HashSet<string>(
            config.Nodes.OfType<LoopNode>().Select(n => n.Id));

        // Collect edges that are "back edges" through Loop nodes.
        // A Loop node's outgoing edges that go back to its body are allowed cycles.
        var loopBackEdges = new HashSet<string>();
        foreach (var loopNode in config.Nodes.OfType<LoopNode>())
        {
            // Any edge from this loop node to a node that feeds into it
            // is part of the loop body (allowed cycle).
            // We identify these by: the edge target is an ancestor of the loop in the DAG.
            MarkLoopBackEdges(loopNode.Id, adj, loopBackEdges, loopNodes);
        }

        // DFS-based topological sort and cycle detection
        var state = new Dictionary<string, NodeColor>(); // White=unvisited, Gray=in-progress, Black=done
        foreach (var nodeId in adj.AllNodes)
            state[nodeId] = NodeColor.White;

        var topo = new List<string>();
        bool hasCycle = false;
        string? cycleNode = null;

        void Dfs(string nodeId)
        {
            if (!state.ContainsKey(nodeId)) return; // ghost node (referenced by edge but not in graph)
            if (state[nodeId] == NodeColor.Black) return;
            if (state[nodeId] == NodeColor.Gray)
            {
                // If all incoming edges to this node come from Loop nodes, it's an allowed cycle
                bool allFromLoop = adj.Incoming[nodeId].All(e =>
                    loopNodes.Contains(e.SourceNodeId) || loopBackEdges.Contains(e.Id));
                if (!allFromLoop && !loopNodes.Contains(nodeId))
                {
                    hasCycle = true;
                    cycleNode = nodeId;
                }
                return;
            }

            state[nodeId] = NodeColor.Gray;

            foreach (var edge in adj.Outgoing[nodeId])
            {
                // Skip edges that are loop back edges
                if (loopBackEdges.Contains(edge.Id)) continue;
                Dfs(edge.TargetNodeId);
            }

            state[nodeId] = NodeColor.Black;
            topo.Add(nodeId);
        }

        // Start DFS from entry nodes
        foreach (var nodeId in adj.EntryNodes)
        {
            Dfs(nodeId);
        }

        // Check for unvisited nodes (disconnected from entry points)
        foreach (var nodeId in adj.AllNodes)
        {
            if (state[nodeId] == NodeColor.White)
            {
                Dfs(nodeId);
                if (state[nodeId] == NodeColor.White)
                {
                    errors.Add(new CompileError
                    {
                        NodeId = nodeId,
                        Code = ErrorCodes.UnreachableNode,
                        Message = $"Node '{nodeId}' is not reachable from any entry point. All nodes must have a path from an entry node (nodes with no incoming edges).",
                    });
                }
            }
        }

        if (hasCycle)
        {
            errors.Add(new CompileError
            {
                NodeId = cycleNode,
                Code = ErrorCodes.CycleWithoutLoop,
                Message = $"Cycle detected involving node '{cycleNode}'. Cycles are only allowed through explicit Loop nodes. Use a Loop node to create intentional cycles.",
            });
        }

        // Check dead-end nodes: non-sink nodes with no path to the sink
        var sinkNode = config.Nodes.OfType<MetricsSinkNode>().FirstOrDefault();
        if (sinkNode != null && !hasCycle)
        {
            // Build reverse reachability from sink
            var reachesSink = new HashSet<string>();
            var sinkQueue = new Queue<string>();
            sinkQueue.Enqueue(sinkNode.Id);
            reachesSink.Add(sinkNode.Id);

            while (sinkQueue.Count > 0)
            {
                var current = sinkQueue.Dequeue();
                foreach (var edge in adj.Incoming[current])
                {
                    if (reachesSink.Add(edge.SourceNodeId))
                        sinkQueue.Enqueue(edge.SourceNodeId);
                }
            }

            // Collect nodes that are loop-body interior nodes — they don't need
            // to reach the sink because the Loop node's exit edge carries their
            // accumulated result out.
            var loopBodyNodes = new HashSet<string>();
            foreach (var loopNode in config.Nodes.OfType<LoopNode>())
            {
                var bodyStartEdge = adj.Outgoing[loopNode.Id]
                    .FirstOrDefault(e => e.SourcePort == "body");
                if (bodyStartEdge == null) continue;

                var bodyQueue = new Queue<string>();
                bodyQueue.Enqueue(bodyStartEdge.TargetNodeId);
                loopBodyNodes.Add(bodyStartEdge.TargetNodeId);

                while (bodyQueue.Count > 0)
                {
                    var curr = bodyQueue.Dequeue();
                    foreach (var edge in adj.Outgoing[curr])
                    {
                        // A nested loop's exit is still inside this body. The owning loop is
                        // outside this traversal, so its own exit is never followed here.
                        if (loopBodyNodes.Add(edge.TargetNodeId))
                            bodyQueue.Enqueue(edge.TargetNodeId);
                    }
                }
            }

            foreach (var nodeId in adj.AllNodes)
            {
                if (nodeId == sinkNode.Id) continue;

                var node = config.Nodes.FirstOrDefault(n => n.Id == nodeId);

                // Skip ghost nodes (referenced by edges but not in config.Nodes)
                // — they've already been reported by ValidateEdgeTypes.
                if (node == null) continue;

                // Skip MetricsSink (already handled)
                if (node is MetricsSinkNode) continue;

                // Skip loop body interior nodes — they terminate in the loop, not at sink
                if (loopBodyNodes.Contains(nodeId)) continue;

                if (!reachesSink.Contains(nodeId))
                {
                    // A node is dead-end if it has no outgoing edges but has incoming edges
                    // (i.e., it's reachable from entry but doesn't go anywhere)
                    if (adj.Outgoing[nodeId].Count == 0)
                    {
                        if (adj.Incoming[nodeId].Count > 0)
                        {
                            errors.Add(new CompileError
                            {
                                NodeId = nodeId,
                                Code = ErrorCodes.DeadEndNode,
                                Message = $"Node '{nodeId}' is a dead end: it receives input but has no outgoing edges leading to the MetricsSink.",
                            });
                        }
                        else
                        {
                            errors.Add(new CompileError
                            {
                                NodeId = nodeId,
                                Code = ErrorCodes.UnreachableNode,
                                Message = $"Node '{nodeId}' is disconnected: it has no incoming or outgoing edges.",
                            });
                        }
                    }
                }
            }
        }
    }

    private static void MarkLoopBackEdges(
        string loopNodeId, AdjacencyInfo adj,
        HashSet<string> loopBackEdges, HashSet<string> loopNodes)
    {
        // Find nodes that are ancestors of this loop node (feed into it)
        var ancestors = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(loopNodeId);
        ancestors.Add(loopNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in adj.Incoming[current])
            {
                if (ancestors.Add(edge.SourceNodeId))
                    queue.Enqueue(edge.SourceNodeId);
            }
        }

        // Any edge from the loop node to an ancestor is a loop back edge
        foreach (var edge in adj.Outgoing[loopNodeId])
        {
            if (ancestors.Contains(edge.TargetNodeId))
                loopBackEdges.Add(edge.Id);
        }
    }

    private enum NodeColor { White, Gray, Black }

    // ── 3. Edge type compatibility ───────────────────────────────────────

    private static void ValidateEdgeTypes(GraphConfig config, List<CompileError> errors)
    {
        var nodeMap = config.Nodes.ToDictionary(n => n.Id);

        foreach (var edge in config.Edges)
        {
            if (!nodeMap.TryGetValue(edge.SourceNodeId, out var sourceNode))
            {
                errors.Add(new CompileError
                {
                    EdgeId = edge.Id,
                    NodeId = edge.SourceNodeId,
                    Code = ErrorCodes.InvalidGraph,
                    Message = $"Edge '{edge.Id}' references unknown source node '{edge.SourceNodeId}'.",
                });
                continue;
            }

            if (!nodeMap.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                errors.Add(new CompileError
                {
                    EdgeId = edge.Id,
                    NodeId = edge.TargetNodeId,
                    Code = ErrorCodes.InvalidGraph,
                    Message = $"Edge '{edge.Id}' references unknown target node '{edge.TargetNodeId}'.",
                });
                continue;
            }

            if (!sourceNode.Outputs.TryGetValue(edge.SourcePort, out var sourcePort))
            {
                errors.Add(new CompileError
                {
                    EdgeId = edge.Id,
                    NodeId = edge.SourceNodeId,
                    Code = ErrorCodes.TypeMismatch,
                    Message = $"Edge '{edge.Id}': source node '{edge.SourceNodeId}' has no output port '{edge.SourcePort}'.",
                });
                continue;
            }

            if (!targetNode.Inputs.TryGetValue(edge.TargetPort, out var targetPort))
            {
                errors.Add(new CompileError
                {
                    EdgeId = edge.Id,
                    NodeId = edge.TargetNodeId,
                    Code = ErrorCodes.TypeMismatch,
                    Message = $"Edge '{edge.Id}': target node '{edge.TargetNodeId}' has no input port '{edge.TargetPort}'.",
                });
                continue;
            }

            if (sourcePort.Type != targetPort.Type)
            {
                // Allow implicit conversions: Symbol → String, Number → Boolean (for triggers)
                if (!IsImplicitConversion(sourcePort.Type, targetPort.Type))
                {
                    errors.Add(new CompileError
                    {
                        EdgeId = edge.Id,
                        NodeId = edge.SourceNodeId,
                        Code = ErrorCodes.TypeMismatch,
                        Message = $"Edge '{edge.Id}': type mismatch — source port '{edge.SourcePort}' is {sourcePort.Type} but target port '{edge.TargetPort}' is {targetPort.Type}.",
                    });
                }
            }
        }
    }

    private static bool IsImplicitConversion(PortType source, PortType target)
    {
        return (source, target) switch
        {
            (PortType.Symbol, PortType.String) => true,
            (PortType.Boolean, PortType.Trigger) => true,
            _ => false,
        };
    }

    // ── 4. Expression type-checking ──────────────────────────────────────

    private static void ValidateExpressions(
        GraphConfig config, TypeCheckContext? typeCheckContext, List<CompileError> errors)
    {
        foreach (var node in config.Nodes)
        {
            var reference = node switch
            {
                DrawNode d => d.WeightExpressionId,
                ModifyStateNode m => m.ExpressionId,
                BranchNode b => b.ConditionId,
                LoopNode l => l.StopConditionId,
                _ => null,
            };
            if (reference is not null && (config.Expressions is null || !config.Expressions.ContainsKey(reference)))
                errors.Add(new CompileError { NodeId = node.Id, Code = ErrorCodes.ExpressionTypeError,
                    Message = $"Expression '{reference}' does not exist." });
        }
        if (config.Expressions == null || config.Expressions.Count == 0) return;

        var ctx = typeCheckContext ?? BuildExpressionTypeContext(config);

        // Build a map from expression name to nodes that reference it
        var exprToNodes = new Dictionary<string, List<string>>();
        foreach (var node in config.Nodes)
        {
            foreach (var (_, port) in node.Inputs)
            {
                if (port.DefaultValue is FieldAccessExpr fa
                    && fa.Target == "state"
                    && fa.Path.Length >= 2
                    && fa.Path[0] == "expressions")
                {
                    var exprName = fa.Path[1];
                    if (!exprToNodes.ContainsKey(exprName))
                        exprToNodes[exprName] = new List<string>();
                    if (!exprToNodes[exprName].Contains(node.Id))
                        exprToNodes[exprName].Add(node.Id);
                }
            }
        }

        // Check named expressions
        foreach (var (name, expr) in config.Expressions)
        {
            // Try to find which node references this expression
            string? referencingNode = null;
            exprToNodes.TryGetValue(name, out var nodes);
            if (nodes is { Count: > 0 })
                referencingNode = nodes[0];

            var exprErrors = ExpressionTypeChecker.Check(expr, ctx);
            foreach (var exprError in exprErrors)
            {
                errors.Add(new CompileError
                {
                    NodeId = referencingNode,
                    Code = ErrorCodes.ExpressionTypeError,
                    Message = $"Expression '{name}': {exprError.Message}",
                });
            }

            // D16 expression cost budget.
            if (!ExpressionCost.WithinBudget(expr, out var cost))
            {
                errors.Add(new CompileError
                {
                    NodeId = referencingNode,
                    Code = ErrorCodes.ExpressionBudgetExceeded,
                    Message =
                        $"Expression '{name}' has static cost {cost}, exceeding the per-leaf budget of " +
                        $"{SlotMathConstants.Expression.MaxOps} operations (D16).",
                });
            }
        }

        // Check expression-valued ports on nodes
        foreach (var node in config.Nodes)
        {
            foreach (var (portName, port) in node.Inputs)
            {
                if (port.DefaultValue != null)
                {
                    var targetType = PortTypeToExprType(port.Type);
                    if (targetType != null)
                    {
                        var exprErrors = ExpressionTypeChecker.Check(port.DefaultValue, ctx, targetType.Value);
                        foreach (var exprError in exprErrors)
                        {
                            errors.Add(new CompileError
                            {
                                NodeId = node.Id,
                                Code = ErrorCodes.ExpressionTypeError,
                                Message = $"Node '{node.Id}', port '{portName}' expression: {exprError.Message}",
                            });
                        }
                    }
                }
            }

            // Check expression references on nodes
            switch (node)
            {
                case DrawNode d when d.WeightExpressionId != null:
                    if (config.Expressions.TryGetValue(d.WeightExpressionId, out var wExpr))
                    {
                        var wErrors = ExpressionTypeChecker.Check(wExpr, ctx);
                        foreach (var e in wErrors)
                            errors.Add(new CompileError { NodeId = node.Id, Code = ErrorCodes.ExpressionTypeError, Message = $"Draw weight expression: {e.Message}" });
                    }
                    break;
                case LoopNode l when l.StopConditionId != null:
                    if (config.Expressions.TryGetValue(l.StopConditionId, out var sExpr))
                    {
                        var sErrors = ExpressionTypeChecker.Check(sExpr, ctx, ExprType.Boolean);
                        foreach (var e in sErrors)
                            errors.Add(new CompileError { NodeId = node.Id, Code = ErrorCodes.ExpressionTypeError, Message = $"Loop stop condition: {e.Message}" });
                    }
                    break;
                case BranchNode b when b.ConditionId != null:
                    if (config.Expressions.TryGetValue(b.ConditionId, out var cExpr))
                    {
                        var cErrors = ExpressionTypeChecker.Check(cExpr, ctx, ExprType.Boolean);
                        foreach (var e in cErrors)
                            errors.Add(new CompileError { NodeId = node.Id, Code = ErrorCodes.ExpressionTypeError, Message = $"Branch condition: {e.Message}" });
                    }
                    break;
            }
        }
    }

    private static ExprType? PortTypeToExprType(PortType portType)
    {
        return portType switch
        {
            PortType.Number => ExprType.Number,
            PortType.Boolean => ExprType.Boolean,
            PortType.String => ExprType.String,
            _ => null,
        };
    }

    /// <summary>
    /// Build a TypeCheckContext that includes named expressions as accessible state fields.
    /// </summary>
    private static TypeCheckContext BuildExpressionTypeContext(GraphConfig config)
    {
        var stateFields = new List<FieldDescriptor>(TypeCheckContext.Default.StateFields);
        var seen = new HashSet<string>(stateFields.Select(f => f.Name));

        // Add a synthetic "expressions" field that resolves expression references
        if (config.Expressions != null)
        {
            stateFields.Add(new FieldDescriptor
            {
                Name = "expressions",
                Type = ExprType.Number,
                Description = "Named expressions accessible via expressions.<name>",
            });
        }

        // Graph Truth → Everything Derived: the recurrence-state schema is derived
        // from what the graph writes (board+dims, data arrays, ModifyState outputs
        // typed by their writer expression, loop counters/accumulators), with
        // author-declared StateSchema entries as optional explicit overrides.
        foreach (var fd in StateSchemaDeriver.Derive(config))
            if (seen.Add(fd.Name))
                stateFields.Add(fd);

        return new TypeCheckContext
        {
            ExpectedType = ExprType.Number,
            BoardFields = TypeCheckContext.Default.BoardFields,
            StateFields = stateFields,
            CellFields = TypeCheckContext.Default.CellFields,
            DecorationTypes = TypeCheckContext.Default.DecorationTypes,
        };
    }

    // ── 5. Plugin validation ─────────────────────────────────────────────

    private static void ValidatePlugins(
        GraphConfig config, PluginHost? pluginHost, List<CompileError> errors)
    {
        // Collect plugin references from nodes
        foreach (var node in config.Nodes)
        {
            if (node is MapNode map && map.TransformId != null && map.TransformId.StartsWith("plugin:"))
            {
                var pluginId = map.TransformId["plugin:".Length..];

                if (pluginHost == null)
                {
                    errors.Add(new CompileError
                    {
                        NodeId = node.Id,
                        Code = ErrorCodes.PluginNotFound,
                        Message = $"Node '{node.Id}' references plugin '{pluginId}' but no PluginHost is available.",
                    });
                    continue;
                }

                // Transform plugins bypass conformance (ITransform has no harness yet)
                if (pluginHost.TryGetTransform(pluginId) != null)
                    continue;

                var (canSelect, reason) = pluginHost.CanSelect(pluginId);
                if (!canSelect)
                {
                    var entry = pluginHost.TryGetEntry(pluginId);
                    if (entry == null)
                    {
                        errors.Add(new CompileError
                        {
                            NodeId = node.Id,
                            Code = ErrorCodes.PluginNotFound,
                            Message = $"Node '{node.Id}' references plugin '{pluginId}' which is not registered.",
                        });
                    }
                    else
                    {
                        errors.Add(new CompileError
                        {
                            NodeId = node.Id,
                            Code = ErrorCodes.PluginNotConformant,
                            Message = $"Node '{node.Id}' references plugin '{pluginId}' which is not conformant: {reason}",
                        });
                    }
                }
            }
        }

        // Also check PluginReference[] in config
        if (config.Plugins != null)
        {
            foreach (var pluginRef in config.Plugins)
            {
                if (pluginHost == null) continue;

                var entry = pluginHost.TryGetEntry(pluginRef.PluginId);
                if (entry == null)
                {
                    // Plugin referenced in config but not found in host
                    // This is reported only if a node actually uses it
                    continue;
                }

                if (!entry.IsConformant)
                {
                    // Find which node uses this plugin
                    foreach (var node in config.Nodes)
                    {
                        if (node is MapNode map && map.TransformId == $"plugin:{pluginRef.PluginId}")
                        {
                            errors.Add(new CompileError
                            {
                                NodeId = node.Id,
                                Code = ErrorCodes.PluginNotConformant,
                                Message = $"Node '{node.Id}' references plugin '{pluginRef.PluginId}' which has not passed conformance validation.",
                            });
                        }
                    }
                }
            }
        }
    }
}
