import { parseExpression, type ExpressionAst } from './expressionParser';
import type { GraphNode, GraphEdge } from '../store';

// ═══════════════════════════════════════════════════════════════════
// Canonical graph → backend config mapping.
//
// Single source of truth for serializing the canvas graph into the
// backend GraphConfig shape.  Used by live metrics, simulate, and any
// other feature that posts a config.
//
// Emit discriminators first for compatibility with older JSON consumers.
// The current backend also accepts metadata after ordinary properties.
// ═══════════════════════════════════════════════════════════════════

/** Map a frontend node to a backend-serializable node object. */
export function mapNodeToBackend(n: GraphNode): Record<string, unknown> {
  const base = { id: n.id, label: n.data.label };
  if (n.data.backendNode && typeof n.data.backendNode === 'object') {
    const original = n.data.backendNode as Record<string, unknown>;
    return { nodeType: original.nodeType, ...original, ...base,
      ...(original.nodeType === 'draw' && n.data.drawWeights ? { drawWeights: n.data.drawWeights } : {}),
      ...(original.nodeType === 'metricsSink' ? { winCap: n.data.winCap ?? original.winCap } : {}) };
  }
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
        outputs: { true: { name: 'true', type: 'Wins' }, false: { name: 'false', type: 'Wins' } },
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
        shareWildAcrossSymbols: n.data.shareWildAcrossSymbols ?? false,
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
    case 'library':
      {
        const fast = ['lines', 'ways', 'cluster'].includes(n.data.mechanicName ?? '');
        const input = fast ? 'board' : 'state'; const output = fast ? 'wins' : 'state';
        return { nodeType: 'library', ...base, mechanicName: n.data.mechanicName, parameters: n.data.parameters ?? {},
          inputs: { [input]: { name: input, type: fast ? 'Board' : 'State' } }, outputs: { [output]: { name: output, type: fast ? 'Wins' : 'State' } } };
      }
    case 'sink':
      return {
        nodeType: 'metricsSink',
        ...base,
        winCap: n.data.winCap ?? 10000,
        ...(n.data.winStateKey ? { winStateKey: n.data.winStateKey } : {}),
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
  tables?: Record<string, unknown>;
}

/** Build a backend config object from graph state. Returns null when the graph is empty. */
export function buildConfigPayload(
  nodes: GraphNode[],
  edges: GraphEdge[],
  options: ConfigPayloadOptions,
): Record<string, unknown> | null {
  if (nodes.length === 0) return null;

  const expressions: Record<string, ExpressionAst> = {};
  for (const [id, text] of Object.entries(options.expressions ?? {})) expressions[id] = parseExpression(text);
  const mapped = nodes.map((node) => {
    const result = mapNodeToBackend(node);
    if (node.data.backendNode) return result;
    const fields = node.data.nodeType === 'branch' ? [['expression', 'conditionId']]
      : node.data.nodeType === 'loop' ? [['terminationExpr', 'stopConditionId']]
      : node.data.nodeType === 'state' && node.data.stateOp === 'modify' ? [['expression', 'expressionId']]
      : node.data.nodeType === 'draw' ? [['weightExpressionId', 'weightExpressionId']] : [];
    for (const [field, ref] of fields) {
      const text = node.data[field];
      if (typeof text === 'string' && text.trim()) {
        if (expressions[text]) result[ref] = text;
        else { const id = `${node.id}:${ref}`; expressions[id] = parseExpression(text); result[ref] = id; }
      } else delete result[ref];
    }
    return result;
  });
  function port(nodeId: string, handle: string | null | undefined, direction: 'in' | 'out') {
    const node = nodes.find(n => n.id === nodeId);
    if (node?.data.nodeType === 'library' && !node.data.backendNode) {
      const fast = ['lines', 'ways', 'cluster'].includes(node.data.mechanicName ?? '');
      return fast ? direction === 'in' ? 'board' : 'wins' : 'state';
    }
    return handle ?? direction;
  }
  return {
    ...options.tables,
    schemaVersion: '1.0.0',
    name: options.name,
    nodes: mapped,
    edges: edges.map((e) => ({
      id: e.id,
      sourceNodeId: e.source,
      sourcePort: port(e.source, e.sourceHandle, 'out'),
      targetNodeId: e.target,
      targetPort: port(e.target, e.targetHandle, 'in'),
    })),
    expressions: { ...(options.tables?.expressions as Record<string, unknown> ?? {}), ...expressions },
  };
}
