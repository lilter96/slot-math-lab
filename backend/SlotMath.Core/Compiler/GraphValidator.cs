using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Compiler;

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

        // 2. Build adjacency and validate cycles / reachability
        var adjacency = BuildAdjacency(config);
        ValidateAcyclicityAndReachability(config, adjacency, errors);

        // 3. Edge type compatibility
        ValidateEdgeTypes(config, errors);

        // 4. Expression type-checking
        ValidateExpressions(config, typeCheckContext, errors);

        // 5. Plugin validation
        ValidatePlugins(config, pluginHost, errors);

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
        }
        else if (sinks.Length > 1)
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
            if (!outgoing.ContainsKey(edge.SourceNodeId))
                outgoing[edge.SourceNodeId] = new List<Edge>();
            if (!incoming.ContainsKey(edge.TargetNodeId))
                incoming[edge.TargetNodeId] = new List<Edge>();

            outgoing[edge.SourceNodeId].Add(edge);
            incoming[edge.TargetNodeId].Add(edge);
        }

        var entryNodes = new HashSet<string>(allNodes.Where(n => incoming[n].Count == 0));

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
            var queue = new Queue<string>();
            queue.Enqueue(sinkNode.Id);
            reachesSink.Add(sinkNode.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var edge in adj.Incoming[current])
                {
                    if (reachesSink.Add(edge.SourceNodeId))
                        queue.Enqueue(edge.SourceNodeId);
                }
            }

            foreach (var nodeId in adj.AllNodes)
            {
                if (nodeId == sinkNode.Id) continue;

                var node = config.Nodes.First(n => n.Id == nodeId);

                // Skip MetricsSink (already handled)
                if (node is MetricsSinkNode) continue;

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
            var exprErrors = ExpressionTypeChecker.Check(expr, ctx);
            foreach (var exprError in exprErrors)
            {
                // Try to find which node references this expression
                string? referencingNode = null;
                exprToNodes.TryGetValue(name, out var nodes);
                if (nodes is { Count: > 0 })
                    referencingNode = nodes[0];

                errors.Add(new CompileError
                {
                    NodeId = referencingNode,
                    Code = ErrorCodes.ExpressionTypeError,
                    Message = $"Expression '{name}': {exprError.Message}",
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
