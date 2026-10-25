import {append, iteration, state, when, type Expr} from './expr';

export type GraphNode = { nodeType: string; id: string; label: string; [key: string]: unknown };

export interface GraphEdge {
    id: string;
    sourceNodeId: string;
    sourcePort: string;
    targetNodeId: string;
    targetPort: string
}

export interface DistributionAsset {
    name?: string;
    outcomes: { outcomeId: string; weight: number; value: number }[]
}

export interface Mechanic {
    name: string;
    description: string;
    nodes: GraphNode[];
    edges: GraphEdge[];
    expressions: Record<string, Expr>;
    distributionAssets?: Record<string, DistributionAsset>
}

export type Parameters = Record<string, string | number>;
/** One outcome of a draw: the value written to the state, and its integer weight. */
export type Outcome = readonly [value: string | number, weight: number];

const statePort = {state: {name: 'state', type: 'State'}};
const ports = (...names: string[]) => Object.fromEntries(names.map(name => [name, {name, type: 'State'}]));

/**
 * Lays out one subgraph as a sequence of steps. It only arranges nodes and edges: every step is an
 * ordinary graph atom, and the result is the same JSON a person would draw on the canvas.
 */
export class Flow {
    private readonly path: GraphNode[] = [];
    private readonly bodies: GraphNode[] = [];
    private readonly edges: GraphEdge[] = [];
    private readonly expressions: Record<string, Expr> = {};
    private readonly batches = new Map<GraphNode, {
        rows: DistributionAsset[];
        schedule: number[];
        count?: number;
        countExpressionId?: string;
        maxCount?: number;
    }>();
    private tail: [nodeId: string, port: string] | undefined;
    private pending: [nodeId: string, port: string][] = [];

    private connect(source: string, port: string, target: string) {
        this.edges.push({
            id: `${source}.${port}->${target}`,
            sourceNodeId: source,
            sourcePort: port,
            targetNodeId: target,
            targetPort: 'state'
        });
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
        return {
            nodeType: 'library', id, label, mechanicName: mechanic, inputs: statePort, outputs: statePort,
            parameters: Object.fromEntries(Object.entries(parameters).map(([key, value]) => [key, String(value)]))
        };
    }

    /** Compute `expression` and store it under `key`. */
    write(id: string, label: string, key: string, expression: Expr): this {
        this.expressions[id] = expression;
        this.step({
            nodeType: 'modifyState',
            id,
            label,
            outputKey: key,
            expressionId: id,
            inputs: statePort,
            outputs: statePort
        });
        return this;
    }

    /** Append `value` to the array `history`, but only in a traced (played) round; analyses leave it empty. */
    record(id: string, label: string, history: string, value: Expr): this {
        return this.write(id, label, history, when(state('traceEnabled'), append(history, value), state(history)));
    }

    /** Draw one of `outcomes` by weight and store its value under `key`. */
    draw(id: string, label: string, key: string, outcomes: readonly Outcome[]): this {
        this.step({
            nodeType: 'draw', id, label, stateWriteKey: key, inputs: statePort, outputs: statePort,
            drawWeights: outcomes.filter(([, weight]) => weight > 0).map(([value, weight]) => ({
                outcomeId: String(value),
                weight,
                value: 0
            }))
        });
        return this;
    }

    /** Repeat one ordered scalar table, committing the results as one String array. */
    drawMany(id: string, label: string, key: string, count: number, outcomes: readonly Outcome[]): this {
        if (!Number.isInteger(count) || count < 0 || count > 4096) throw new Error('An ordered draw count must be an integer from 0 to 4096');
        return this.batch(id, label, key, count === 0 ? [] : [outcomes], count === 0 ? undefined : count);
    }

    /** Draw an expression-bounded number of samples from one ordered distribution. */
    drawManyDynamic(id: string, label: string, key: string, count: Expr, maxCount: number,
                    outcomes: readonly Outcome[]): this {
        if (!Number.isInteger(maxCount) || maxCount < 0 || maxCount > 4096)
            throw new Error('A dynamic ordered draw maximum must be an integer from 0 to 4096');
        const countExpressionId = `${id}-count`;
        this.expressions[countExpressionId] = count;
        return this.batch(id, label, key, [outcomes], undefined, {countExpressionId, maxCount});
    }

    /** An explicit schedule: each table draws once, in this exact order. */
    drawManySchedule(id: string, label: string, key: string, schedule: readonly (readonly Outcome[])[]): this {
        if (schedule.length > 4096) throw new Error('An ordered draw schedule accepts at most 4096 entries');
        return this.batch(id, label, key, schedule);
    }

    private batch(id: string, label: string, key: string, source: readonly (readonly Outcome[])[], count?: number,
                  dynamic?: { countExpressionId: string; maxCount: number }): this {
        const rows: DistributionAsset[] = [], schedule: number[] = [],
            identities = new Map<readonly Outcome[], number>();
        for (const outcomes of source) {
            let row = identities.get(outcomes);
            if (row === undefined) {
                row = rows.length;
                identities.set(outcomes, row);
                // Preserve the same positive rows, payload strings and order as draw().
                rows.push({
                    name: label, outcomes: outcomes.filter(([, weight]) => weight > 0)
                        .map(([value, weight]) => ({outcomeId: String(value), weight, value: 0}))
                });
            }
            schedule.push(row);
        }
        const node: GraphNode = {
            nodeType: 'drawMany',
            id,
            label,
            outputKey: key,
            inputs: statePort,
            outputs: statePort
        };
        this.step(node);
        this.batches.set(node, {rows, schedule, ...(count === undefined ? {} : {count}), ...dynamic});
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
        this.step({nodeType: 'branch', id, label, conditionId: id, inputs: statePort, outputs: ports('true', 'false')});
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
    either(id: string, label: string, condition: Expr, yes: (path: Flow) => void, no: (path: Flow) => void = () => {
    }): this {
        this.expressions[id] = condition;
        this.step({nodeType: 'branch', id, label, conditionId: id, inputs: statePort, outputs: ports('true', 'false')});
        const ends: [nodeId: string, port: string][] = [];
        for (const [port, lay] of [['true', yes], ['false', no]] as const) {
            const path = new Flow();
            lay(path);
            const first = path.path[0];
            if (!first) {
                ends.push([id, port]);
                continue;
            }
            for (const node of [...path.path, ...path.bodies]) {
                if ([...this.path, ...this.bodies].some(existing => existing.id === node.id)) throw new Error(`Duplicate node id '${node.id}'`);
                this.bodies.push(node);
            }
            for (const [node, batch] of path.batches) this.batches.set(node, batch);
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
        this.step({
            nodeType: 'loop',
            id,
            label,
            maxIterations: times, ...(options.until ? {stopConditionId: `${id}-until`} : {}),
            inputs: statePort,
            outputs: ports('body', 'exit')
        });
        const body = this.placed(`${id}-pass`, options.bodyLabel ?? label, mechanic, options.parameters ?? {});
        if (options.counter) {
            const turn: GraphNode = {
                nodeType: 'modifyState', id: `${id}-turn`, label: 'Which pass this is, counted from 0',
                outputKey: options.counter, expressionId: `${id}-turn`, inputs: statePort, outputs: statePort
            };
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
        this.step({nodeType: 'metricsSink', id, label, winStateKey: winKey, winCap, inputs: statePort, outputs: {}});
        return this;
    }

    private parts(scope: string) {
        // Nodes on the main path come first: a subgraph's outgoing boundary is its first node whose `state`
        // output is unused, which must be the end of the main path and not the end of a loop or branch body.
        const distributions: Record<string, DistributionAsset> = {};
        const nodes = [...this.path, ...this.bodies].map(node => {
            const batch = this.batches.get(node);
            if (!batch) return node;
            const ids = batch.rows.map((row, index) => {
                const id = `${scope}/${encodeURIComponent(node.id)}/row-${index + 1}`;
                distributions[id] = {...row, outcomes: row.outcomes.map(outcome => ({...outcome}))};
                return id;
            });
            return {
                ...node,
                ...(batch.countExpressionId !== undefined
                    ? {distributionId: ids[0], countExpressionId: batch.countExpressionId, maxCount: batch.maxCount}
                    : batch.count === undefined
                        ? {distributionIds: batch.schedule.map(index => ids[index])}
                        : {distributionId: ids[0], count: batch.count})
            };
        });
        return {
            nodes, edges: this.edges, expressions: this.expressions,
            ...(Object.keys(distributions).length ? {distributions} : {})
        };
    }

    /** Finish this flow as a named, reusable subgraph. */
    mechanic(name: string, description: string): Mechanic {
        if (this.tail?.[1] !== 'state') this.step({
            nodeType: 'getState',
            id: 'done',
            label: 'Done',
            inputs: statePort,
            outputs: statePort
        });
        const {distributions, ...parts} = this.parts(`mechanic/${encodeURIComponent(name)}`);
        return {name, description, ...parts, ...(distributions ? {distributionAssets: distributions} : {})};
    }

    /** Finish this flow as the top level of a game graph. */
    root(name = 'root') {
        return this.parts(`root/${encodeURIComponent(name)}`);
    }
}

/** The subgraphs of one game. A shared subgraph may be added by several features; it is stored once. */
export class Mechanics {
    private readonly byName = new Map<string, Mechanic>();
    private readonly assets: Record<string, DistributionAsset> = {};

    add(mechanic: Mechanic): string {
        const {distributionAssets = {}, ...body} = mechanic;
        const existing = this.byName.get(mechanic.name);
        if (existing && JSON.stringify(existing) !== JSON.stringify(body)) throw new Error(`Two different subgraphs are both named '${mechanic.name}'`);
        for (const [id, asset] of Object.entries(distributionAssets)) {
            if (this.assets[id] && JSON.stringify(this.assets[id]) !== JSON.stringify(asset)) throw new Error(`Two different categorical tables are both named '${id}'`);
        }
        Object.assign(this.assets, structuredClone(distributionAssets));
        this.byName.set(mechanic.name, body);
        return mechanic.name;
    }

    all(): Record<string, Mechanic> {
        return Object.fromEntries(this.byName);
    }

    distributions(): Record<string, DistributionAsset> {
        return structuredClone(this.assets);
    }
}

export interface GameGraphParts {
    id: string;
    name: string;
    description: string;
    root: Flow;
    mechanics: Mechanics;
    initialState: Record<string, unknown>;
    tables?: Record<string, unknown>;
}

/** Assemble a complete graph config. `traceEnabled` is switched on by the play endpoint; analyses run with it off. */
export function gameGraph({
                              id,
                              name,
                              description,
                              root,
                              mechanics,
                              initialState,
                              tables = {}
                          }: GameGraphParts): Record<string, unknown> {
    const parts = root.root(id), distributions = mechanics.distributions();
    const merge = (source: Record<string, DistributionAsset>) => {
        for (const [key, asset] of Object.entries(source)) {
            if (distributions[key] && JSON.stringify(distributions[key]) !== JSON.stringify(asset)) throw new Error(`Two different categorical tables are both named '${key}'`);
            distributions[key] = structuredClone(asset);
        }
    };
    merge(parts.distributions ?? {});
    if (tables.distributions !== undefined && tables.distributions !== null) {
        if (typeof tables.distributions !== 'object' || Array.isArray(tables.distributions)) throw new Error('Categorical tables must be an object map');
        merge(tables.distributions as Record<string, DistributionAsset>);
    }
    return {
        schemaVersion: '1.0.0',
        id,
        name,
        description,
        initialState: {traceEnabled: false, emptyArray: [], ...initialState},
        ...parts,
        mechanics: mechanics.all(), ...tables, ...(Object.keys(distributions).length ? {distributions} : {})
    };
}
