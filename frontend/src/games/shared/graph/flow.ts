import { append, iteration, state, when, type Expr } from './expr';

export type GraphNode = { nodeType: string; id: string; label: string; [key: string]: unknown };
export interface GraphEdge { id: string; sourceNodeId: string; sourcePort: string; targetNodeId: string; targetPort: string }
export interface Mechanic { name: string; description: string; nodes: GraphNode[]; edges: GraphEdge[]; expressions: Record<string, Expr> }
export type Parameters = Record<string, string | number>;
/** One outcome of a draw: the value written to the state, and its integer weight. */
export type Outcome = readonly [value: string | number, weight: number];

const statePort = { state: { name: 'state', type: 'State' } };
const ports = (...names: string[]) => Object.fromEntries(names.map(name => [name, { name, type: 'State' }]));

/**
 * Lays out one subgraph as a sequence of steps. It only arranges nodes and edges: every step is an
 * ordinary graph atom, and the result is the same JSON a person would draw on the canvas.
 */
export class Flow {
  private readonly path: GraphNode[] = [];
  private readonly bodies: GraphNode[] = [];
  private readonly edges: GraphEdge[] = [];
  private readonly expressions: Record<string, Expr> = {};
  private tail: [nodeId: string, port: string] | undefined;
  private pending: [nodeId: string, port: string][] = [];

  private connect(source: string, port: string, target: string) {
    this.edges.push({ id: `${source}.${port}->${target}`, sourceNodeId: source, sourcePort: port, targetNodeId: target, targetPort: 'state' });
  }
  private step(node: GraphNode) {
    if ([...this.path, ...this.bodies].some(existing => existing.id === node.id)) throw new Error(`Duplicate node id '${node.id}'`);
    for (const [source, port] of this.ends()) this.connect(source, port, node.id);
    this.pending = [];
    this.path.push(node);
    this.tail = [node.id, 'state'];
  }
  private ends(): [nodeId: string, port: string][] {
    return this.pending.length ? this.pending : this.tail ? [this.tail] : [];
  }
  private placed(id: string, label: string, mechanic: string, parameters: Parameters): GraphNode {
    return { nodeType: 'library', id, label, mechanicName: mechanic, inputs: statePort, outputs: statePort,
      parameters: Object.fromEntries(Object.entries(parameters).map(([key, value]) => [key, String(value)])) };
  }

  /** Compute `expression` and store it under `key`. */
  write(id: string, label: string, key: string, expression: Expr): this {
    this.expressions[id] = expression;
    this.step({ nodeType: 'modifyState', id, label, outputKey: key, expressionId: id, inputs: statePort, outputs: statePort });
    return this;
  }
  /** Append `value` to the array `history`, but only in a traced (played) round; analyses leave it empty. */
  record(id: string, label: string, history: string, value: Expr): this {
    return this.write(id, label, history, when(state('traceEnabled'), append(history, value), state(history)));
  }
  /** Draw one of `outcomes` by weight and store its value under `key`. */
  draw(id: string, label: string, key: string, outcomes: readonly Outcome[]): this {
    this.step({ nodeType: 'draw', id, label, stateWriteKey: key, inputs: statePort, outputs: statePort,
      drawWeights: outcomes.filter(([, weight]) => weight > 0).map(([value, weight]) => ({ outcomeId: String(value), weight, value: 0 })) });
    return this;
  }
  /** Run another subgraph here. */
  use(id: string, label: string, mechanic: string, parameters: Parameters = {}): this {
    this.step(this.placed(id, label, mechanic, parameters));
    return this;
  }
  /** Run `mechanic` only when `condition` holds; both paths continue with the next step. */
  onlyIf(id: string, label: string, condition: Expr, mechanic: string, parameters: Parameters = {}): this {
    this.expressions[id] = condition;
    this.step({ nodeType: 'branch', id, label, conditionId: id, inputs: statePort, outputs: ports('true', 'false') });
    const body = this.placed(`${id}-yes`, label, mechanic, parameters);
    this.bodies.push(body);
    this.connect(id, 'true', body.id);
    this.pending = [[id, 'false'], [body.id, 'state']];
    this.tail = undefined;
    return this;
  }
  /**
   * Take one of two paths, each a few steps laid out by its callback; both continue with the next step.
   * A path without steps does nothing.
   */
  either(id: string, label: string, condition: Expr, yes: (path: Flow) => void, no: (path: Flow) => void = () => {}): this {
    this.expressions[id] = condition;
    this.step({ nodeType: 'branch', id, label, conditionId: id, inputs: statePort, outputs: ports('true', 'false') });
    const ends: [nodeId: string, port: string][] = [];
    for (const [port, lay] of [['true', yes], ['false', no]] as const) {
      const path = new Flow();
      lay(path);
      const first = path.path[0];
      if (!first) { ends.push([id, port]); continue; }
      for (const node of [...path.path, ...path.bodies]) {
        if ([...this.path, ...this.bodies].some(existing => existing.id === node.id)) throw new Error(`Duplicate node id '${node.id}'`);
        this.bodies.push(node);
      }
      this.edges.push(...path.edges);
      Object.assign(this.expressions, path.expressions);
      this.connect(id, port, first.id);
      ends.push(...path.ends());
    }
    this.pending = ends;
    this.tail = undefined;
    return this;
  }
  /**
   * Run `mechanic` up to `times` times, stopping early once `until` holds (checked before every pass). With
   * `counter`, every pass starts by storing its number, counted from 0, under that key for the subgraph to read.
   */
  repeat(id: string, label: string, times: number, mechanic: string,
    options: { until?: Expr; parameters?: Parameters; bodyLabel?: string; counter?: string } = {}): this {
    if (options.until) this.expressions[`${id}-until`] = options.until;
    this.step({ nodeType: 'loop', id, label, maxIterations: times, ...(options.until ? { stopConditionId: `${id}-until` } : {}),
      inputs: statePort, outputs: ports('body', 'exit') });
    const body = this.placed(`${id}-pass`, options.bodyLabel ?? label, mechanic, options.parameters ?? {});
    if (options.counter) {
      const turn: GraphNode = { nodeType: 'modifyState', id: `${id}-turn`, label: 'Which pass this is, counted from 0',
        outputKey: options.counter, expressionId: `${id}-turn`, inputs: statePort, outputs: statePort };
      this.expressions[turn.id] = iteration(id);
      this.bodies.push(turn);
      this.connect(id, 'body', turn.id);
      this.connect(turn.id, 'state', body.id);
    } else this.connect(id, 'body', body.id);
    this.bodies.push(body);
    this.tail = [id, 'exit'];
    return this;
  }
  /** Report `state[winKey]` as the round's payout, limited to `winCap`. Ends the top-level graph. */
  sink(id: string, label: string, winKey: string, winCap: number): this {
    this.step({ nodeType: 'metricsSink', id, label, winStateKey: winKey, winCap, inputs: statePort, outputs: {} });
    return this;
  }

  private parts() {
    // Nodes on the main path come first: a subgraph's outgoing boundary is its first node whose `state`
    // output is unused, which must be the end of the main path and not the end of a loop or branch body.
    return { nodes: [...this.path, ...this.bodies], edges: this.edges, expressions: this.expressions };
  }
  /** Finish this flow as a named, reusable subgraph. */
  mechanic(name: string, description: string): Mechanic {
    if (this.tail?.[1] !== 'state') this.step({ nodeType: 'getState', id: 'done', label: 'Done', inputs: statePort, outputs: statePort });
    return { name, description, ...this.parts() };
  }
  /** Finish this flow as the top level of a game graph. */
  root() { return this.parts(); }
}

/** The subgraphs of one game. A shared subgraph may be added by several features; it is stored once. */
export class Mechanics {
  private readonly byName = new Map<string, Mechanic>();
  add(mechanic: Mechanic): string {
    const existing = this.byName.get(mechanic.name);
    if (existing && JSON.stringify(existing) !== JSON.stringify(mechanic)) throw new Error(`Two different subgraphs are both named '${mechanic.name}'`);
    this.byName.set(mechanic.name, mechanic);
    return mechanic.name;
  }
  all(): Record<string, Mechanic> { return Object.fromEntries(this.byName); }
}

export interface GameGraphParts {
  id: string; name: string; description: string;
  root: Flow; mechanics: Mechanics;
  initialState: Record<string, unknown>;
  tables?: Record<string, unknown>;
}
/** Assemble a complete graph config. `traceEnabled` is switched on by the play endpoint; analyses run with it off. */
export function gameGraph({ id, name, description, root, mechanics, initialState, tables = {} }: GameGraphParts): Record<string, unknown> {
  return { schemaVersion: '1.0.0', id, name, description, initialState: { traceEnabled: false, emptyArray: [], ...initialState },
    ...root.root(), mechanics: mechanics.all(), ...tables };
}
