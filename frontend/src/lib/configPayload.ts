import type { GraphNode, GraphEdge } from '../store';

// ═══════════════════════════════════════════════════════════════════
// Canonical graph → backend config mapping.
//
// Single source of truth for serializing the canvas graph into the
// backend GraphConfig shape.  Used by live metrics, simulate, and any
// other feature that posts a config.
//
// IMPORTANT: `nodeType` must be the FIRST property of every node
// object — the backend uses System.Text.Json's [JsonPolymorphic]
// streaming deserializer, which requires the discriminator before all
// other properties.
// ═══════════════════════════════════════════════════════════════════

/** Map a frontend node to a backend-serializable node object. */
export function mapNodeToBackend(n: GraphNode): Record<string, unknown> {
  const base = { id: n.id, label: n.data.label };
  switch (n.data.nodeType) {
    case 'draw':
      return {
        nodeType: 'draw',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.drawWeights?.length ? { drawWeights: n.data.drawWeights } : {}),
        ...(n.data.weightExpressionId ? { weightExpressionId: n.data.weightExpressionId } : {}),
        ...(n.data.stateWriteKey ? { stateWriteKey: n.data.stateWriteKey } : {}),
      };
    case 'state': {
      const op = (n.data.stateOp as string) ?? 'get';
      const key = (n.data.stateKey as string) || '__default__';
      if (op === 'put') {
        return {
          nodeType: 'putState',
          ...base,
          stateKey: key,
          inputs: { in: { name: 'in', type: 'Wins' } },
          outputs: { out: { name: 'out', type: 'Wins' } },
        };
      }
      if (op === 'modify') {
        return {
          nodeType: 'modifyState',
          ...base,
          expressionId: (n.data.expression as string) ?? undefined,
          inputs: { in: { name: 'in', type: 'Wins' } },
          outputs: { out: { name: 'out', type: 'Wins' } },
        };
      }
      return {
        nodeType: 'getState',
        ...base,
        stateKey: key,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
      };
    }
    case 'loop':
      return {
        nodeType: 'loop',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: {
          body: { name: 'body', type: 'Wins' },
          exit: { name: 'exit', type: 'Wins' },
        },
        maxIterations: (n.data.iterations as number) ?? 5,
        ...(n.data.terminationExpr ? { stopConditionId: n.data.terminationExpr } : {}),
      };
    case 'branch':
      return {
        nodeType: 'branch',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.expression ? { conditionId: n.data.expression } : {}),
      };
    case 'map':
      // Note: a Map node's transformId is a transform/evaluator id — an
      // expression name is NOT a valid transformId and must not be
      // substituted for one.
      return {
        nodeType: 'map',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        ...(n.data.transformId ? { transformId: n.data.transformId } : {}),
      };
    case 'evaluator': {
      const kind = (n.data.evaluatorKind as string) ?? 'lines';
      const transformId = kind === 'plugin'
        ? `plugin:${(n.data.pluginId as string) ?? ''}`
        : kind;
      return {
        nodeType: 'map',
        ...base,
        inputs: { in: { name: 'in', type: 'Board' } },
        outputs: { out: { name: 'out', type: 'Wins' } },
        transformId,
      };
    }
    case 'transform':
      return {
        nodeType: 'map',
        ...base,
        inputs: { in: { name: 'in', type: 'Board' } },
        outputs: { out: { name: 'out', type: 'Board' } },
        ...(n.data.sub ? { transformId: n.data.sub } : {}),
      };
    case 'sink':
      return {
        nodeType: 'metricsSink',
        ...base,
        inputs: { in: { name: 'in', type: 'Wins' } },
        outputs: {},
      };
    default:
      return {
        nodeType: n.data.nodeType,
        ...base,
        inputs: {},
        outputs: { out: { name: 'out', type: 'Wins' } },
      };
  }
}

export interface ConfigPayloadOptions {
  name: string;
  expressions?: Record<string, string>;
}

/** Build a backend config object from graph state. Returns null when the graph is empty. */
export function buildConfigPayload(
  nodes: GraphNode[],
  edges: GraphEdge[],
  options: ConfigPayloadOptions,
): Record<string, unknown> | null {
  if (nodes.length === 0) return null;

  const expressions = options.expressions ?? {};
  return {
    schemaVersion: '1.0.0',
    name: options.name,
    nodes: nodes.map(mapNodeToBackend),
    edges: edges.map((e) => ({
      id: e.id,
      sourceNodeId: e.source,
      sourcePort: e.sourceHandle ?? 'out',
      targetNodeId: e.target,
      targetPort: e.targetHandle ?? 'in',
    })),
    expressions: Object.keys(expressions).length > 0 ? expressions : undefined,
  };
}
