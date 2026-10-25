import {iteration, state, itemAt, gte, length} from '../../games/shared/graph/expr';
import type {ExpressionAst} from '../expressionParser';
import type {ModelGraph, ModelNode} from '../modelProjection';
import {reelObject} from '../reelCommands';
import {resolveNodeSource, type NodeSource} from './sourceIndex';

export interface CollectionRepeatSpec {
    call: string;
    name: string;
    sourceKey: string;
    itemKey: string;
    indexKey: string;
    itemType: 'Number' | 'String' | 'Boolean' | 'Array' | 'Record';
    maxItems: number;
}

const statePort = () => ({state: {name: 'state', type: 'State'}});
const edge = (from: string, to: string, port = 'state') => ({
    id: `${from}.${port}->${to}`, sourceNodeId: from, sourcePort: port, targetNodeId: to, targetPort: 'state'
});
const serviceKey = (key: string) => ['__iter_', '__wins_', '__exitReason_'].some(prefix => key.startsWith(prefix));
const key = (value: string) => value.length > 0 && value.length <= 128 && value.trim() === value && !serviceKey(value);

/** The wrapper has one State input/output, so it can replace this call without reconnecting external edges. */
export function collectionRepeatCalls(graph: ModelGraph): NodeSource[] {
    const scopes = [[null, graph], ...Object.entries(graph.mechanics ?? {})] as const;
    return scopes.flatMap(([scope, definition]) => {
        const nodes = definition.nodes ?? [], counts = new Map<string, number>();
        for (const node of nodes) counts.set(node.id, (counts.get(node.id) ?? 0) + 1);
        return nodes.flatMap(node => {
            if (counts.get(node.id) !== 1) return [];
            const ports = (value: unknown) => {
                const object = reelObject(value);
                return !!object && Object.keys(object).length === 1 && reelObject(object.state)?.type === 'State';
            };
            return node.nodeType === 'library' && ports(node.inputs) && ports(node.outputs)
                ? [resolveNodeSource(graph, JSON.stringify([scope, node.id]))] : [];
        });
    });
}

/** Lower designer repetition into editable existing atoms. No executable repeat metadata or kernel pattern is added. */
export function repeatCollection(graph: ModelGraph, spec: CollectionRepeatSpec): ModelGraph {
    const call = collectionRepeatCalls(graph).find(call => call.key === spec.call);
    if (!call) throw new Error('Choose a subgraph call with one State input and one State output.');
    if (!key(spec.name) || graph.mechanics?.[spec.name]) throw new Error('Choose a new, unique repeat definition name.');
    if (![spec.sourceKey, spec.itemKey, spec.indexKey].every(key) || new Set([spec.sourceKey, spec.itemKey, spec.indexKey]).size !== 3) throw new Error('Collection, current item and index need distinct state keys, each 1–128 characters; loop service keys are reserved.');
    if (!Number.isSafeInteger(spec.maxItems) || spec.maxItems < 0 || spec.maxItems > 4096) throw new Error('The collection bound must be a whole number in 0–4096.');
    if (!['Number', 'String', 'Boolean', 'Array', 'Record'].includes(spec.itemType)) throw new Error('Choose the collection item type.');
    const snapshotKey = `__repeat_${spec.name}_source`;
    // A private generated source must not collide with any authored key, including opaque source data.
    const contains = (value: unknown): boolean => value === snapshotKey || (!!value && typeof value === 'object'
        && Object.entries(value).some(([name, child]) => name === snapshotKey || contains(child)));
    if (snapshotKey.length > 128 || contains(graph)) throw new Error('The generated collection snapshot key is already in use. Choose another repeat definition name.');
    const visited = new Set<string>(), active = new Set<string>();
    const body = (name: string) => {
        if (active.size >= 50) throw new Error('The body exceeds the supported 50 levels of subgraph nesting.');
        if (active.has(name)) throw new Error('The body contains a recursive subgraph reference.');
        if (visited.has(name)) return;
        const definition = graph.mechanics?.[name];
        if (!definition) throw new Error('The repeat body references a missing subgraph.');
        active.add(name);
        for (const node of definition.nodes) {
            const writes: unknown[] = [];
            if (node.nodeType === 'modifyState') writes.push(node.outputKey ?? '__modified__');
            if (node.nodeType === 'drawMany') writes.push(node.outputKey);
            if (node.nodeType === 'draw') {
                writes.push(node.stateWriteKey, node.boardStateKey);
                if (!(Array.isArray(node.drawWeights) && node.drawWeights.length) && !node.distributionId && !node.distributionIds) writes.push(node.boardStateKey ?? 'board', 'rows', 'cols');
            }
            if (node.nodeType === 'data' || node.nodeType === 'putState') writes.push(node.stateKey);
            if (writes.some(value => typeof value === 'string' && serviceKey(value))) throw new Error('The body writes a reserved loop service key. Keep loop control in ordinary Loop nodes.');
            if (writes.some(key => key === spec.itemKey || key === spec.indexKey)) throw new Error('The body writes a current-item or index key. Use distinct bindings for nested repeats.');
            if (node.nodeType === 'library') body(String(node.mechanicName));
            if (!['modifyState', 'draw', 'drawMany', 'data', 'putState', 'getState', 'loop', 'branch', 'library', 'metricsSink'].includes(node.nodeType)) throw new Error('The body has an unresolved write effect. Use an editable atomic body before binding a collection.');
        }
        active.delete(name);
        visited.add(name);
    };
    body(String(call.node.mechanicName));
    const write = (id: string, label: string, outputKey: string): ModelNode => ({
        nodeType: 'modifyState', id, label, expressionId: id, outputKey, inputs: statePort(), outputs: statePort()
    });
    const expressions: Record<string, ExpressionAst> = {
        snapshot: {
            exprType: 'map',
            stateKey: spec.sourceKey,
            maxItems: spec.maxItems,
            itemName: 'entry',
            itemType: spec.itemType,
            body: state('entry')
        },
        finished: gte(iteration('each'), length(snapshotKey)),
        index: iteration('each'),
        item: itemAt(state(snapshotKey), state(spec.indexKey)),
    };
    const wrapper = {
        name: spec.name,
        description: `For each ${spec.itemType} in ${spec.sourceKey}; snapshot once, bind ${spec.itemKey} and ${spec.indexKey}; at most ${spec.maxItems} items.`,
        nodes: [
            write('snapshot', `Snapshot ${spec.sourceKey}`, snapshotKey),
            {
                nodeType: 'loop',
                id: 'each',
                label: `For each item in ${spec.sourceKey}`,
                maxIterations: spec.maxItems + 1,
                stopConditionId: 'finished',
                inputs: statePort(),
                outputs: {body: {name: 'body', type: 'State'}, exit: {name: 'exit', type: 'State'}}
            },
            {nodeType: 'getState', id: 'done', label: 'Collection complete', inputs: statePort(), outputs: statePort()},
            write('index', `Bind ${spec.indexKey}`, spec.indexKey),
            write('item', `Bind ${spec.itemKey}`, spec.itemKey),
            {...structuredClone(call.node), id: 'body'},
        ],
        edges: [edge('snapshot', 'each'), edge('each', 'done', 'exit'), edge('each', 'index', 'body'), edge('index', 'item'), edge('item', 'body')],
        expressions,
    };
    const next = structuredClone(graph);
    if (next.stateSchema !== undefined && !Array.isArray(next.stateSchema)) throw new Error('The authored state schema must be a field list.');
    const schema = (next.stateSchema ?? []) as { name: string; type: string; [key: string]: unknown }[];
    for (const [name, type] of [[spec.itemKey, spec.itemType.toLowerCase()], [spec.indexKey, 'number']]) {
        const declarations = schema.filter(field => field.name === name);
        if (declarations.length > 1 || declarations.some(field => field.type !== type)) throw new Error('A current-item or index binding conflicts with an authored state type. Choose compatible bindings.');
        if (!declarations.length) schema.push({name, type});
    }
    next.stateSchema = schema;
    const replacement = resolveNodeSource(next, spec.call).node as ModelNode;
    replacement.mechanicName = spec.name;
    replacement.parameters = {};
    replacement.label = `For each item in ${spec.sourceKey}`;
    next.mechanics = {...next.mechanics, [spec.name]: wrapper};
    return next;
}
