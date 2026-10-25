import type {ModelDefinition, ModelGraph, ModelNode} from '../modelProjection';
import {canonicalJson} from '../hash';
import {reelObject} from '../reelCommands';
import {indexGraphSources, type NodeSource, sourceKey} from './sourceIndex';
import {machineExpressionText} from './machineLowering';
import type {Expr} from '../../games/shared/graph/expr';

export interface ControlRegion {
    key: string;
    label: string;
    kind: 'state' | 'decision' | 'loop' | 'operation' | 'finish';
    entry: boolean;
    boundary: boolean;
    actions: NodeSource[];
    nested: string | null
}

export interface ControlTransition {
    key: string;
    source: string;
    target: string;
    label: string;
    nodeKey: string | null;
    expressionId: string | null;
    scope: string | null;
    port: string;
    virtual: boolean
}

export interface ControlProjection {
    scope: string | null;
    revision: string;
    regions: ControlRegion[];
    transitions: ControlTransition[];
    issues: string[]
}

const statePort = (node: ModelNode, field: 'inputs' | 'outputs', port: string) => reelObject(reelObject(node[field])?.[port])?.type === 'State';

export function expressionLabel(value: unknown): string {
    try {
        return machineExpressionText(value as Expr).replace(/state\["([^"\\]+)"\]/g, '$1');
    } catch {
        return 'Custom expression · inspect its original graph source';
    }
}

const graphScope = (graph: ModelGraph, scope: string | null): ModelGraph => scope === null ? graph : graph.mechanics?.[scope] ?? {};

export function controlRevision(graph: ModelGraph, scope: string | null): string {
    const index = indexGraphSources(graph), definition = graphScope(graph, scope);
    return canonicalJson({
        definition,
        initialState: graph.initialState,
        uses: scope === null ? [] : index.libraryUses.filter(use => use.definition === scope).map(use => [use.source.key, use.source.node])
    });
}

/** Display-only projection of authored sequences/branches/loops. It never rewrites imports into program counters. */
export function projectControl(graph: ModelGraph, scope: string | null = null): ControlProjection {
    const definition = graphScope(graph, scope), nodes = definition.nodes ?? [], index = indexGraphSources(graph),
        sources = index.nodes.filter(source => source.scope === scope), issues: string[] = [];
    const edges = Array.isArray(definition.edges) ? definition.edges as Record<string, unknown>[] : [],
        incoming = (id: string) => edges.filter(edge => edge.targetNodeId === id),
        outgoing = (id: string) => edges.filter(edge => edge.sourceNodeId === id);
    if (!nodes.length) issues.push('This source has no available control nodes.');
    if (new Set(nodes.map(node => node.id)).size !== nodes.length) issues.push('Duplicate node identities: inspect the original graph before editing.');
    const sequential = (node: ModelNode) => ['modifyState', 'draw', 'getState', 'data', 'putState'].includes(node.nodeType) && statePort(node, 'inputs', 'state') && statePort(node, 'outputs', 'state');
    const predecessors = new Map<string, string>(), successors = new Map<string, string>();
    for (const node of nodes.filter(sequential)) {
        const next = outgoing(node.id);
        if (next.length !== 1 || next[0].sourcePort !== 'state' || next[0].targetPort !== 'state') continue;
        const target = nodes.find(node => node.id === next[0].targetNodeId);
        if (!target || !sequential(target) || incoming(target.id).length !== 1) continue;
        predecessors.set(target.id, node.id);
        successors.set(node.id, target.id);
    }
    const assigned = new Map<string, string>(), regions: ControlRegion[] = [];
    const append = (node: ModelNode) => {
        if (assigned.has(node.id)) return;
        const key = sourceKey(scope, node.id), actions: NodeSource[] = [], visited = new Set<string>();
        let cursor: ModelNode | undefined = node;
        while (cursor && !assigned.has(cursor.id)) {
            if (visited.has(cursor.id)) {
                issues.push('A raw cycle is retained as custom source; use a structured Loop when authoring new repetition.');
                break;
            }
            visited.add(cursor.id);
            assigned.set(cursor.id, key);
            const source = sources.find(source => source.id === cursor!.id);
            if (source) actions.push(source);
            cursor = successors.has(cursor.id) ? nodes.find(node => node.id === successors.get(cursor!.id)) : undefined;
        }
        const structured = statePort(node, 'inputs', 'state'),
            kind: ControlRegion['kind'] = node.nodeType === 'metricsSink' ? 'finish' : node.nodeType === 'branch' && structured ? 'decision' : node.nodeType === 'loop' && structured ? 'loop' : node.nodeType === 'library' && structured ? 'state' : sequential(node) ? 'state' : 'operation';
        const last = actions.at(-1)?.node ?? node;
        regions.push({
            key,
            label: actions.length > 1 ? `${String(node.label ?? node.id)} · ${actions.length} ordered actions` : String(node.label ?? node.id),
            kind,
            entry: incoming(node.id).length === 0,
            boundary: node.nodeType === 'metricsSink' || statePort(last, 'outputs', 'state') && outgoing(last.id).length === 0,
            actions,
            nested: node.nodeType === 'library' && typeof node.mechanicName === 'string' ? node.mechanicName : null
        });
    };
    nodes.filter(node => !predecessors.has(node.id)).forEach(append);
    nodes.forEach(append);
    const transitions: ControlTransition[] = [];
    for (const edge of edges) {
        const from = nodes.find(node => node.id === edge.sourceNodeId),
            source = assigned.get(String(edge.sourceNodeId)), target = assigned.get(String(edge.targetNodeId));
        if (!from || !source || !target) {
            issues.push(`Connection ${String(edge.id)} has a missing endpoint.`);
            continue;
        }
        if (source === target) continue;
        const port = String(edge.sourcePort),
            conditionId = from.nodeType === 'branch' && typeof from.conditionId === 'string' ? from.conditionId : from.nodeType === 'loop' && typeof from.stopConditionId === 'string' ? from.stopConditionId : null;
        const condition = conditionId ? reelObject(definition.expressions)?.[conditionId] : undefined;
        const label = from.nodeType === 'branch' ? `${port === 'true' ? 'If' : 'Otherwise'} ${expressionLabel(condition)}` : from.nodeType === 'loop' ? port === 'body' ? 'Next pass · existing Loop body' : condition ? `Finish when ${expressionLabel(condition)} or the iteration limit` : 'Finish at the iteration limit' : 'Next';
        transitions.push({
            key: JSON.stringify([scope, edge.id]),
            source,
            target,
            label,
            nodeKey: conditionId ? sourceKey(scope, from.id) : null,
            expressionId: conditionId,
            scope,
            port,
            virtual: false
        });
        if (from.nodeType === 'loop' && port === 'body') transitions.push({
            key: JSON.stringify([scope, edge.id, 'return']),
            source: target,
            target: source,
            label: 'Return through feature boundary · next pass',
            nodeKey: null,
            expressionId: null,
            scope,
            port: 'return',
            virtual: true
        });
    }
    return {scope, revision: controlRevision(graph, scope), regions, transitions, issues: [...new Set(issues)]};
}

export function controlScopes(graph: ModelGraph) {
    const index = indexGraphSources(graph);
    return [{
        key: null,
        label: 'Current round',
        uses: 0
    }, ...Object.entries(graph.mechanics ?? {}).map(([key, value]) => ({
        key,
        label: String(value.description ?? key),
        uses: index.libraryUses.filter(use => use.definition === key).length
    }))];
}

export function controlSource(graph: ModelGraph, scope: string | null): ModelDefinition | ModelGraph {
    return graphScope(graph, scope);
}
