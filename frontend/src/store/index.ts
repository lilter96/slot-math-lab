import { create } from 'zustand';
import { LIMITS } from '../lib/limits';
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

export interface DrawWeightEntry {
  outcomeId: string;
  weight: number;
  /** Numeric value produced when this outcome is drawn. Any integer — not necessarily monetary. */
  value: number;
}

export interface GraphNodeData {
  label: string;
  nodeType: 'draw' | 'state' | 'loop' | 'branch' | 'map' | 'evaluator' | 'transform' | 'library' | 'sink';
  sub?: string;
  level?: 'a' | 'b' | 'c';
  /** Loop-specific config */
  iterations?: number;
  terminationExpr?: string;
  /** Evaluator config */
  evaluatorKind?: 'lines' | 'ways' | 'cluster' | 'scatter' | 'plugin';
  /** Expression on weight/multiplier ports */
  expression?: string;
  /** Draw node: inline weighted outcomes (no ReelSets needed) */
  drawWeights?: DrawWeightEntry[];
  /** State node operation */
  stateOp?: 'get' | 'put' | 'modify';
  /** State key for Get/Put/Modify nodes */
  stateKey?: string;
  /** Transform/evaluator ID for Map nodes (level a: 'lines'|'ways'|etc; level c: 'plugin:pluginId') */
  transformId?: string;
  /** Weight expression ID for Draw level (b) */
  weightExpressionId?: string;
  /** Draw node: when set, writes the drawn outcomeId to this state key */
  stateWriteKey?: string;
  /** Plugin ID when evaluatorKind === 'plugin' */
  pluginId?: string;
  /** Library (catalog mechanic) node: the catalog mechanic name */
  mechanicName?: string;
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
  limitError: string | null;
  setLimitError: (err: string | null) => void;
  /** Auth state for multi-tenancy */
  isAuthenticated: boolean;
  userDisplayName: string | null;
  setAuth: (authenticated: boolean, name?: string | null) => void;
}

export const NODE_DEFAULTS: Record<string, Partial<GraphNodeData>> = {
  draw: { label: 'Draw', sub: 'Weighted choice', level: 'a' },
  state: { label: 'State', sub: 'Get / Put / Modify', level: 'a', stateOp: 'get', stateKey: '' },
  loop: { label: 'Loop', sub: 'Fixpoint + stop', level: 'a', iterations: 5, terminationExpr: '' },
  branch: { label: 'Branch', sub: 'Bind + conditional', level: 'a' },
  map: { label: 'Map', sub: 'Transform result', level: 'a' },
  evaluator: { label: 'Evaluator', sub: 'IEvaluator', level: 'a', evaluatorKind: 'lines' },
  transform: { label: 'Transform', sub: 'ITransform', level: 'a' },
  sink: { label: 'Sink', sub: 'Metrics output', level: 'a' },
  // Catalog (library) mechanics — one entry per built-in subgraph
  'library:scatter':     { label: 'Scatter',      sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'scatter' },
  'library:lines':       { label: 'Lines',         sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'lines' },
  'library:ways':        { label: 'Ways',          sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'ways' },
  'library:cascade':     { label: 'Cascade',       sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'cascade' },
  'library:sticky-wild': { label: 'Sticky Wild',   sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'sticky-wild' },
  'library:hold-and-win':{ label: 'Hold & Win',    sub: 'Catalog mechanic', level: 'a', nodeType: 'library', mechanicName: 'hold-and-win' },
};

// ── Connection validation ──────────────────────────────────────────

/** Valid port-to-port connections. sink has no output. */
const VALID_CONNECTIONS: Record<string, string[]> = {
  draw: ['evaluator', 'transform', 'library', 'loop', 'branch'],
  state: ['draw', 'evaluator', 'transform', 'library', 'loop', 'branch', 'map', 'sink'],
  loop: ['evaluator', 'transform', 'library', 'draw', 'branch', 'map', 'sink'],
  branch: ['evaluator', 'transform', 'library', 'draw', 'loop', 'map', 'sink'],
  map: ['evaluator', 'transform', 'library', 'draw', 'loop', 'branch', 'sink'],
  evaluator: ['transform', 'library', 'loop', 'branch', 'map', 'sink'],
  transform: ['evaluator', 'transform', 'library', 'loop', 'branch', 'map', 'sink'],
  library: ['evaluator', 'transform', 'library', 'loop', 'branch', 'map', 'sink'],
  sink: [],
};

export function validateConnection(
  sourceType: string,
  targetType: string,
): string | null {
  if (sourceType === 'sink') {
    return 'Sink nodes have no output ports.';
  }
  const valid = VALID_CONNECTIONS[sourceType];
  if (!valid || !valid.includes(targetType)) {
    return `Cannot connect ${sourceType} → ${targetType}. Valid targets for ${sourceType}: ${(valid || []).join(', ') || 'none'}.`;
  }
  return null;
}

// ── Custom Mechanics ───────────────────────────────────────────────

export interface CustomMechanic {
  id: string;
  name: string;
  description?: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
  createdAt: string;
}

export interface MechanicsState {
  mechanics: CustomMechanic[];
  addMechanic: (m: CustomMechanic) => void;
  removeMechanic: (id: string) => void;
}

// ── Plugins ────────────────────────────────────────────────────────

export type PluginContract = 'IEvaluator' | 'ITransform' | 'WeightSource';

export interface PluginEntry {
  pluginId: string;
  contract: PluginContract;
  version?: string;
  isConformant: boolean;
  conformanceNote?: string;
  /** When a plugin is used, the game must be flagged sampled-regime */
  forcesSampledRegime: boolean;
}

export interface PluginsState {
  plugins: PluginEntry[];
  registerPlugin: (p: PluginEntry) => void;
  removePlugin: (id: string) => void;
  setPluginsFromBackend: (plugins: PluginEntry[]) => void;
  backendAvailable: boolean;
  setBackendAvailable: (v: boolean) => void;
}

export interface MechanicsSyncState {
  setMechanicsFromBackend: (mechanics: CustomMechanic[]) => void;
}

// ── Store ──────────────────────────────────────────────────────────

export interface TableSymbol {
  id: string;
  name: string;
  kind: string;
  color: string;
}

export interface TableState {
  tableSymbols: TableSymbol[];
  setTableSymbols: (s: TableSymbol[]) => void;
}

export interface AppState extends GraphState, MechanicsState, PluginsState, MechanicsSyncState, TableState {
  tab: TabId;
  setTab: (tab: TabId) => void;
  configName: string | null;
  setConfigName: (name: string | null) => void;
  tweaks: Tweaks;
  setTweak: <K extends keyof Tweaks>(key: K, value: Tweaks[K]) => void;
  /** Live metrics from /evaluate/light — shown on sink node and MetricStrip */
  liveRtp: number | null;
  liveProvenance: 'Exact' | 'Sampled' | 'NeedsFullRun' | null;
  setLiveMetrics: (rtp: number | null, provenance: 'Exact' | 'Sampled' | 'NeedsFullRun' | null) => void;
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

  // ── Live metrics ─────────────────────────────────────────────────
  liveRtp: null,
  liveProvenance: null,
  setLiveMetrics: (rtp, provenance) => set({ liveRtp: rtp, liveProvenance: provenance }),

  // ── Graph state ─────────────────────────────────────────────────
  nodes: [],
  edges: [],
  selectedNodeId: null,
  selectNode: (id) => set({ selectedNodeId: id }),
  edgeValidationError: null,
  setEdgeValidationError: (err) => set({ edgeValidationError: err }),
  limitError: null,
  setLimitError: (err) => set({ limitError: err }),
  isAuthenticated: false,
  userDisplayName: null,
  setAuth: (authenticated, name) => set({ isAuthenticated: authenticated, userDisplayName: name ?? null }),

  onNodesChange: (changes: NodeChange<GraphNode>[]) =>
    set((s) => ({ nodes: applyNodeChanges(changes, s.nodes) })),

  onEdgesChange: (changes: EdgeChange<GraphEdge>[]) =>
    set((s) => ({ edges: applyEdgeChanges(changes, s.edges) })),

  addNode: (node) => {
    const s = get();
    if (s.nodes.length >= LIMITS.maxNodes) {
      set({ limitError: `Cannot add node: max ${LIMITS.maxNodes} nodes reached. Simplify the graph.` });
      return;
    }
    set({ nodes: [...s.nodes, node], limitError: null });
  },

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

    const edgeCount = get().edges.length;
    if (edgeCount >= LIMITS.maxEdges) {
      set({ limitError: `Cannot add edge: max ${LIMITS.maxEdges} edges reached. Reduce connections.` });
      return;
    }

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

  // ── Mechanics ───────────────────────────────────────────────────
  mechanics: [],
  addMechanic: (m) => set((s) => ({ mechanics: [...s.mechanics, m] })),
  removeMechanic: (id) => set((s) => ({ mechanics: s.mechanics.filter((m) => m.id !== id) })),

  // ── Plugins ─────────────────────────────────────────────────────
  plugins: [],
  registerPlugin: (p) => set((s) => ({ plugins: [...s.plugins, p] })),
  removePlugin: (id) => set((s) => ({ plugins: s.plugins.filter((p) => p.pluginId !== id) })),
  setPluginsFromBackend: (plugins) => set({ plugins }),
  backendAvailable: false,
  setBackendAvailable: (v) => set({ backendAvailable: v }),
  setMechanicsFromBackend: (mechanics) => set({ mechanics }),

  // ── Table data (used by live metrics) ───────────────────────────
  tableSymbols: [],
  setTableSymbols: (symbols) => set({ tableSymbols: symbols }),
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
