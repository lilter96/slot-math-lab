import { create } from 'zustand';
import type { Node, Edge, Connection, OnNodesChange, OnEdgesChange, NodeChange, EdgeChange } from '@xyflow/react';

export type TabId = 'build' | 'simulate' | 'results' | 'export';
export type Mood = 'slate' | 'ocean' | 'violet' | 'steel';
export type Density = 'compact' | 'regular';

export interface Tweaks {
  accent: string;
  mood: Mood;
  grid: boolean;
  flow: boolean;
  density: Density;
}

// ── Graph state (for @xyflow/react canvas) ──────────────────────────

export interface GraphNodeData {
  label: string;
  nodeType: 'draw' | 'state' | 'loop' | 'branch' | 'map' | 'evaluator' | 'transform' | 'sink';
  sub?: string;
  level?: 'a' | 'b' | 'c';
  /** Loop-specific config */
  iterations?: number;
  terminationExpr?: string;
  /** Evaluator config */
  evaluatorKind?: 'lines' | 'ways' | 'cluster' | 'scatter';
  /** Expression on weight/multiplier ports */
  expression?: string;
  [key: string]: unknown;
}

export type GraphNode = Node<GraphNodeData>;
export type GraphEdge = Edge<{ metric?: string }>;

export interface GraphState {
  nodes: GraphNode[];
  edges: GraphEdge[];
  selectedNodeId: string | null;
  selectNode: (id: string | null) => void;
  onNodesChange: OnNodesChange<GraphNode>;
  onEdgesChange: OnEdgesChange<GraphEdge>;
  addNode: (node: GraphNode) => void;
  removeNode: (id: string) => void;
  onConnect: (connection: Connection) => void;
  setNodeData: (id: string, data: Partial<GraphNodeData>) => void;
  edgeValidationError: string | null;
  setEdgeValidationError: (err: string | null) => void;
}

export const NODE_DEFAULTS: Record<string, Partial<GraphNodeData>> = {
  draw: { label: 'Draw', sub: 'Weighted choice', level: 'a' },
  state: { label: 'State', sub: 'Get / Put / Modify', level: 'a' },
  loop: { label: 'Loop', sub: 'Fixpoint + stop', level: 'a', iterations: 5, terminationExpr: '' },
  branch: { label: 'Branch', sub: 'Bind + conditional', level: 'a' },
  map: { label: 'Map', sub: 'Transform result', level: 'a' },
  evaluator: { label: 'Evaluator', sub: 'IEvaluator', level: 'a', evaluatorKind: 'lines' },
  transform: { label: 'Transform', sub: 'ITransform', level: 'a' },
  sink: { label: 'Sink', sub: 'Metrics output', level: 'a' },
};

// ── Connection validation ──────────────────────────────────────────

/** Valid port-to-port connections. sink has no output, draw has no input. */
const VALID_CONNECTIONS: Record<string, string[]> = {
  draw: ['evaluator', 'transform', 'loop', 'branch'],
  state: ['draw', 'evaluator', 'transform', 'loop', 'branch', 'map', 'sink'],
  loop: ['evaluator', 'transform', 'draw', 'branch', 'map', 'sink'],
  branch: ['evaluator', 'transform', 'draw', 'loop', 'map', 'sink'],
  map: ['evaluator', 'transform', 'draw', 'loop', 'branch', 'sink'],
  evaluator: ['transform', 'loop', 'branch', 'map', 'sink'],
  transform: ['evaluator', 'transform', 'loop', 'branch', 'map', 'sink'],
  sink: [],
};

export function validateConnection(
  sourceType: string,
  targetType: string,
): string | null {
  if (sourceType === 'sink') {
    return 'Sink nodes have no output ports.';
  }
  if (targetType === 'draw') {
    return 'Draw nodes have no input ports.';
  }
  const valid = VALID_CONNECTIONS[sourceType];
  if (!valid || !valid.includes(targetType)) {
    return `Cannot connect ${sourceType} → ${targetType}. Valid targets for ${sourceType}: ${(valid || []).join(', ') || 'none'}.`;
  }
  return null;
}

// ── Store ──────────────────────────────────────────────────────────

export interface AppState extends GraphState {
  tab: TabId;
  setTab: (tab: TabId) => void;
  configName: string | null;
  setConfigName: (name: string | null) => void;
  tweaks: Tweaks;
  setTweak: <K extends keyof Tweaks>(key: K, value: Tweaks[K]) => void;
}

export const MOOD_HUE: Record<Mood, number> = {
  slate: 255,
  ocean: 230,
  violet: 290,
  steel: 210,
};

export const useAppStore = create<AppState>((set, get) => ({
  tab: 'build',
  setTab: (tab) => set({ tab }),
  configName: 'Untitled',
  setConfigName: (name) => set({ configName: name }),
  tweaks: {
    accent: '#46d39a',
    mood: 'slate',
    grid: true,
    flow: true,
    density: 'regular',
  },
  setTweak: (key, value) =>
    set((s) => ({ tweaks: { ...s.tweaks, [key]: value } })),

  // ── Graph state ─────────────────────────────────────────────────
  nodes: [],
  edges: [],
  selectedNodeId: null,
  selectNode: (id) => set({ selectedNodeId: id }),
  edgeValidationError: null,
  setEdgeValidationError: (err) => set({ edgeValidationError: err }),

  onNodesChange: (changes: NodeChange<GraphNode>[]) =>
    set((s) => ({ nodes: applyNodeChanges(changes, s.nodes) })),

  onEdgesChange: (changes: EdgeChange<GraphEdge>[]) =>
    set((s) => ({ edges: applyEdgeChanges(changes, s.edges) })),

  addNode: (node) => set((s) => ({ nodes: [...s.nodes, node] })),

  removeNode: (id) =>
    set((s) => ({
      nodes: s.nodes.filter((n) => n.id !== id),
      edges: s.edges.filter((e) => e.source !== id && e.target !== id),
      selectedNodeId: s.selectedNodeId === id ? null : s.selectedNodeId,
    })),

  onConnect: (connection) => {
    const { nodes, setEdgeValidationError } = get();
    const source = nodes.find((n) => n.id === connection.source);
    const target = nodes.find((n) => n.id === connection.target);
    if (!source || !target) return;

    const err = validateConnection(
      source.data?.nodeType ?? 'draw',
      target.data?.nodeType ?? 'sink',
    );
    if (err) {
      setEdgeValidationError(err);
      return;
    }
    setEdgeValidationError(null);
    set((s) => ({
      edges: [
        ...s.edges,
        {
          id: `e-${connection.source}-${connection.target}`,
          source: connection.source,
          target: connection.target,
          sourceHandle: connection.sourceHandle ?? undefined,
          targetHandle: connection.targetHandle ?? undefined,
          type: 'provenance',
        } as GraphEdge,
      ],
    }));
  },

  setNodeData: (id, data) =>
    set((s) => ({
      nodes: s.nodes.map((n) =>
        n.id === id ? { ...n, data: { ...n.data, ...data } } : n,
      ),
    })),
}));

// ── Minimal Node/Edge change handlers (avoid heavy immer dependency) ─

function applyNodeChanges(
  changes: NodeChange<GraphNode>[],
  nodes: GraphNode[],
): GraphNode[] {
  let next = [...nodes];
  for (const ch of changes) {
    if (ch.type === 'position' && ch.position) {
      const idx = next.findIndex((n) => n.id === ch.id);
      if (idx >= 0) {
        next[idx] = { ...next[idx], position: ch.position };
      }
    } else if (ch.type === 'remove') {
      next = next.filter((n) => n.id !== ch.id);
    }
  }
  return next;
}

function applyEdgeChanges(
  changes: EdgeChange<GraphEdge>[],
  edges: GraphEdge[],
): GraphEdge[] {
  let next = [...edges];
  for (const ch of changes) {
    if (ch.type === 'remove') {
      next = next.filter((e) => e.id !== ch.id);
    }
  }
  return next;
}
