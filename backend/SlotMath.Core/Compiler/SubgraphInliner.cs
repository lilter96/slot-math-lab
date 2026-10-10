using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  SubgraphInliner — expands LibraryNode references into their constituent
//  atoms before compilation (G9).
//
//  A named subgraph (CustomMechanic) is data: nodes + edges + expressions,
//  with typed input/output ports.  A LibraryNode places it on the canvas and
//  wires its ports.  Inlining is a pure graph→graph macro expansion:
//
//    • every mechanic node / edge / expression id is namespaced by the
//      LibraryNode id, so the SAME mechanic can be used many times with no
//      collision;
//    • boundary edges are rewired BY PORT NAME — an external edge into
//      `L.port` is retargeted to the internal node that consumes that input
//      port; an edge out of `L.port` is re-sourced from the internal node that
//      produces that output port (the convention in 12-custom-mechanic.json);
//    • expression cross-references (`state.expressions.<id>`) and node
//      expression-ref fields are rewritten to the namespaced ids;
//    • LibraryNode.Parameters are injected as namespaced constant expressions
//      the mechanic can reference as `state.expressions.<param>`.
//
//  Nesting is handled by iterating to a fixpoint: each pass expands one level
//  of library nodes.  A pass cap bounds the work and turns a cyclic mechanic
//  reference (A → B → A) into a precise error instead of an infinite loop.
//
//  The result contains only primitive nodes, which the existing ProgramBuilder
//  already compiles in full — no interpreter or compiler-core change is needed
//  to add a catalog mechanic (invariant 2).
// ═══════════════════════════════════════════════════════════════════════════

public static class SubgraphInliner
{
    /// <summary>Maximum expansion passes; also the effective nesting-depth cap.</summary>
    public const int MaxPasses = 50;

    /// <summary>
    /// Expand every LibraryNode in the config to its mechanic's atoms.
    /// Returns the flattened config and any errors (missing mechanic,
    /// excessive nesting).  When the config has no LibraryNodes it is
    /// returned unchanged.
    /// </summary>
    public static (GraphConfig Config, IReadOnlyList<CompileError> Errors) Inline(GraphConfig config)
    {
        if (!config.Nodes.OfType<LibraryNode>().Any())
            return (config, Array.Empty<CompileError>());

        var current = config;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            if (!current.Nodes.OfType<LibraryNode>().Any())
                return (current, Array.Empty<CompileError>());

            var (expanded, errors) = ExpandOnce(current);
            if (errors.Count > 0)
                return (current, errors);
            current = expanded;
        }

        // Still library nodes after the cap → cyclic / pathologically deep.
        var offender = current.Nodes.OfType<LibraryNode>().First();
        return (current, new[]
        {
            new CompileError
            {
                NodeId = offender.Id,
                Code = ErrorCodes.InvalidGraph,
                Message =
                    $"Subgraph nesting exceeded {MaxPasses} levels at library node " +
                    $"'{offender.Id}' (mechanic '{offender.MechanicName}'). " +
                    "This usually means a mechanic references itself (directly or transitively).",
            },
        });
    }

    // ── One expansion level ──────────────────────────────────────────────

    private static (GraphConfig Config, IReadOnlyList<CompileError> Errors) ExpandOnce(GraphConfig config)
    {
        var errors = new List<CompileError>();
        var nodes = new List<Node>();
        var edges = new List<Edge>(config.Edges);
        var expressions = config.Expressions is null
            ? new Dictionary<string, Expression>()
            : new Dictionary<string, Expression>(config.Expressions);

        foreach (var node in config.Nodes)
        {
            if (node is not LibraryNode lib)
            {
                nodes.Add(node);
                continue;
            }

            if (config.Mechanics is null
                || !config.Mechanics.TryGetValue(lib.MechanicName, out var mechanic))
            {
                errors.Add(new CompileError
                {
                    NodeId = lib.Id,
                    Code = ErrorCodes.InvalidGraph,
                    Message = $"Library node '{lib.Id}' references unknown mechanic '{lib.MechanicName}'.",
                });
                continue;
            }

            ExpandLibraryNode(lib, mechanic, nodes, edges, expressions, errors);
        }

        if (errors.Count > 0)
            return (config, errors);

        return (config with { Nodes = nodes.ToArray(), Edges = edges.ToArray(), Expressions = expressions }, errors);
    }

    private static void ExpandLibraryNode(
        LibraryNode lib,
        CustomMechanic mechanic,
        List<Node> nodes,
        List<Edge> edges,
        Dictionary<string, Expression> expressions,
        List<CompileError> errors)
    {
        var prefix = lib.Id + "/";

        // 1. Parameters become namespaced constant expressions the mechanic can
        //    reference as state.expressions.<param>.
        foreach (var (paramName, rawValue) in lib.Parameters)
            expressions[prefix + paramName] = ParameterConstant(rawValue);

        // 2. Namespace + ref-rewrite the mechanic's own expressions.
        if (mechanic.Expressions != null)
        {
            foreach (var (key, expr) in mechanic.Expressions)
                expressions[prefix + key] = RewriteExprRefs(expr, prefix);
        }

        // 3. Clone mechanic nodes with namespaced ids and rewritten expr refs.
        foreach (var n in mechanic.Nodes)
            nodes.Add(CloneNode(n, prefix));

        // 4. Clone mechanic edges with namespaced ids.
        foreach (var e in mechanic.Edges)
        {
            edges.Add(e with
            {
                Id = prefix + e.Id,
                SourceNodeId = prefix + e.SourceNodeId,
                TargetNodeId = prefix + e.TargetNodeId,
            });
        }

        // 5. Boundary rewiring.  Remove every external edge touching the
        //    library node and reconnect it to the mechanic's boundary nodes,
        //    matched by port name.
        var external = edges
            .Where(e => e.SourceNodeId == lib.Id || e.TargetNodeId == lib.Id)
            .ToList();
        foreach (var e in external)
            edges.Remove(e);

        foreach (var e in external)
        {
            // Incoming: X.sp → lib.tp  ⇒  X.sp → <internal consumer of tp>
            if (e.TargetNodeId == lib.Id)
            {
                var consumers = InputConsumers(mechanic, e.TargetPort);
                if (consumers.Count == 0)
                {
                    errors.Add(BoundaryError(lib, e.TargetPort, isInput: true));
                    continue;
                }

                foreach (var (nodeId, portName) in consumers)
                {
                    edges.Add(e with
                    {
                        Id = $"{prefix}in:{e.Id}->{nodeId}",
                        TargetNodeId = prefix + nodeId,
                        TargetPort = portName,
                    });
                }
            }

            // Outgoing: lib.sp → Y.tp  ⇒  <internal producer of sp> → Y.tp
            if (e.SourceNodeId == lib.Id)
            {
                var producer = OutputProducer(mechanic, e.SourcePort);
                if (producer is null)
                {
                    errors.Add(BoundaryError(lib, e.SourcePort, isInput: false));
                    continue;
                }

                edges.Add(e with
                {
                    Id = $"{prefix}out:{e.Id}",
                    SourceNodeId = prefix + producer.Value.NodeId,
                    SourcePort = producer.Value.PortName,
                });
            }
        }
    }

    // ── Boundary resolution (by port name) ───────────────────────────────

    /// <summary>
    /// Internal nodes that consume the given input port from outside the
    /// mechanic — i.e. they declare an input port of that name and no internal
    /// edge feeds it.  A single external input may fan out to several.
    /// </summary>
    private static List<(string NodeId, string PortName)> InputConsumers(
        CustomMechanic mechanic, string portName)
    {
        var result = new List<(string, string)>();
        foreach (var n in mechanic.Nodes)
        {
            if (!n.Inputs.ContainsKey(portName))
                continue;
            var fedInternally = mechanic.Edges.Any(e =>
                e.TargetNodeId == n.Id && e.TargetPort == portName);
            if (!fedInternally)
                result.Add((n.Id, portName));
        }
        return result;
    }

    /// <summary>
    /// The internal node that produces the given output port to the outside —
    /// it declares an output port of that name and no internal edge consumes it.
    /// </summary>
    private static (string NodeId, string PortName)? OutputProducer(
        CustomMechanic mechanic, string portName)
    {
        foreach (var n in mechanic.Nodes)
        {
            if (!n.Outputs.ContainsKey(portName))
                continue;
            var consumedInternally = mechanic.Edges.Any(e =>
                e.SourceNodeId == n.Id && e.SourcePort == portName);
            if (!consumedInternally)
                return (n.Id, portName);
        }
        return null;
    }

    private static CompileError BoundaryError(LibraryNode lib, string portName, bool isInput) =>
        new()
        {
            NodeId = lib.Id,
            Code = ErrorCodes.InvalidGraph,
            Message =
                $"Library node '{lib.Id}' (mechanic '{lib.MechanicName}') has no internal " +
                $"{(isInput ? "consumer for input" : "producer for output")} port '{portName}'.",
        };

    // ── Node cloning ─────────────────────────────────────────────────────

    private static Node CloneNode(Node node, string prefix)
    {
        var id = prefix + node.Id;
        var inputs = ClonePorts(node.Inputs, prefix);
        var outputs = ClonePorts(node.Outputs, prefix);

        return node switch
        {
            DrawNode d => d with
            {
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
                WeightExpressionId = NsRef(d.WeightExpressionId, prefix),
            },
            GetStateNode g => g with { Id = id, Inputs = inputs, Outputs = outputs },
            PutStateNode p => p with { Id = id, Inputs = inputs, Outputs = outputs },
            ModifyStateNode m => m with
            {
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
                ExpressionId = NsRef(m.ExpressionId, prefix),
            },
            LoopNode l => l with
            {
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
                StopConditionId = NsRef(l.StopConditionId, prefix),
                ExitReason = l.ExitReason is null ? null : RewriteExprRefs(l.ExitReason, prefix),
            },
            BranchNode b => b with
            {
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
                ConditionId = NsRef(b.ConditionId, prefix),
            },
            MapNode map => map with
            {
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
                // plugin:<id> references are global ids, not mechanic-local.
            },
            DataNode dn => dn with { Id = id, Inputs = inputs, Outputs = outputs },
            MetricsSinkNode s => s with { Id = id, Inputs = inputs, Outputs = outputs },
            LibraryNode lib => lib with
            {
                // Nested library node: namespace its id; it is expanded next pass.
                Id = id,
                Inputs = inputs,
                Outputs = outputs,
            },
            _ => node,
        };
    }

    private static Dictionary<string, Port> ClonePorts(Dictionary<string, Port> ports, string prefix)
    {
        if (ports.Count == 0)
            return new Dictionary<string, Port>();

        var result = new Dictionary<string, Port>(ports.Count);
        foreach (var (key, port) in ports)
        {
            result[key] = port.DefaultValue is null
                ? port
                : port with { DefaultValue = RewriteExprRefs(port.DefaultValue, prefix) };
        }
        return result;
    }

    private static string? NsRef(string? id, string prefix) =>
        id is null ? null : prefix + id;

    // ── Parameter → constant ─────────────────────────────────────────────

    private static ConstantExpr ParameterConstant(string raw)
    {
        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return new ConstantExpr { Kind = ConstantKind.Integer, Value = raw };
        if (raw is "true" or "false")
            return new ConstantExpr { Kind = ConstantKind.Boolean, Value = raw };
        if (raw.Contains('/'))
        {
            var parts = raw.Split('/');
            if (parts.Length == 2
                && long.TryParse(parts[0], out _) && long.TryParse(parts[1], out _))
                return new ConstantExpr { Kind = ConstantKind.Rational, Value = raw };
        }
        return new ConstantExpr { Kind = ConstantKind.String, Value = raw };
    }

    // ── Expression reference rewriting ───────────────────────────────────

    /// <summary>
    /// Rewrite `state.expressions.&lt;id&gt;` references inside an expression
    /// tree to point at the namespaced id, so a mechanic's internal expression
    /// cross-references survive inlining (and don't collide across instances).
    /// </summary>
    private static Expression RewriteExprRefs(Expression expr, string prefix)
    {
        return expr switch
        {
            FieldAccessExpr f => RewriteFieldAccess(f, prefix),
            BinaryExpr b => b with
            {
                Left = RewriteExprRefs(b.Left, prefix),
                Right = RewriteExprRefs(b.Right, prefix),
            },
            CompareExpr c => c with
            {
                Left = RewriteExprRefs(c.Left, prefix),
                Right = RewriteExprRefs(c.Right, prefix),
            },
            IfExpr i => i with
            {
                Condition = RewriteExprRefs(i.Condition, prefix),
                ThenExpr = RewriteExprRefs(i.ThenExpr, prefix),
                ElseExpr = RewriteExprRefs(i.ElseExpr, prefix),
            },
            NotExpr n => n with { Expr = RewriteExprRefs(n.Expr, prefix) },
            AggregateExpr a => a with { Predicate = a.Predicate is null ? null : RewriteExprRefs(a.Predicate, prefix),
                ValueExpr = a.ValueExpr is null ? null : RewriteExprRefs(a.ValueExpr, prefix) },
            CallExpr call => call with
            {
                Args = call.Args.Select(arg => RewriteExprRefs(arg, prefix)).ToArray(),
            },
            FoldExpr fold => fold with
            {
                Init = RewriteExprRefs(fold.Init, prefix),
                Body = RewriteExprRefs(fold.Body, prefix),
            },
            _ => expr, // ConstantExpr and anything leaf-like
        };
    }

    private static Expression RewriteFieldAccess(FieldAccessExpr f, string prefix)
    {
        // Compiler-owned loop counters belong to the loop instance, just like expression IDs.
        if ((f.Target is "state" or null) && f.Path.Length == 1)
            foreach (var marker in new[] { "__iter_", "__wins_" })
                if (f.Path[0].StartsWith(marker, StringComparison.Ordinal) && f.Path[0].EndsWith("__", StringComparison.Ordinal))
                    return f with { Path = [marker + prefix + f.Path[0][marker.Length..]] };
        // state.expressions.<id>  →  state.expressions.<prefix+id>
        if ((f.Target is "state" or null)
            && f.Path.Length >= 2
            && f.Path[0] == "expressions")
        {
            var newPath = (string[])f.Path.Clone();
            newPath[1] = prefix + f.Path[1];
            return f with { Path = newPath };
        }
        return f;
    }
}
