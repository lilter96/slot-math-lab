import type {ModelGraph, ModelNode} from '../modelProjection';
import {visitStateReferences} from './expressionBindings';

export type SourcePath = readonly (string | number)[];

export interface NodeSource {
    key: string;
    scope: string | null;
    id: string;
    path: SourcePath;
    node: Readonly<ModelNode>;
}

export interface ExpressionSource {
    key: string;
    scope: string | null;
    id: string;
    path: SourcePath;
    value: unknown
}

export interface StateRead {
    path: SourcePath;
    statePath: SourcePath
}

export interface LibraryUse {
    source: NodeSource;
    definition: string;
    present: boolean
}

export interface GraphSourceIndex {
    nodes: NodeSource[];
    expressions: ExpressionSource[];
    stateReads: StateRead[];
    stateWrites: { source: NodeSource; key: string }[];
    libraryUses: LibraryUse[];
    issues: { path: SourcePath; message: string }[];
    unresolvedEffects: {
        path: SourcePath;
        scope: string | null;
        reads: boolean;
        writes: boolean;
        active: boolean;
        message: string
    }[];
    expressionReferences: {
        path: SourcePath;
        scope: string | null;
        id: string;
        resolved: boolean;
        statePaths: SourcePath[]
    }[];
}

export const sourceKey = (scope: string | null, id: string) => JSON.stringify([scope, id]);
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

/** A read-only structural inventory, not a recognizer/evaluator for named games. */
export function indexGraphSources(graph: ModelGraph): GraphSourceIndex {
    const index: GraphSourceIndex = {
        nodes: [],
        expressions: [],
        stateReads: [],
        stateWrites: [],
        libraryUses: [],
        issues: [],
        unresolvedEffects: [],
        expressionReferences: []
    };
    const definitions = graph.mechanics ?? {};
    const activeScopes = new Set<string>(),
        queue = (graph.nodes ?? []).filter(node => node.nodeType === 'library').map(node => String(node.mechanicName));
    while (queue.length) {
        const scope = queue.pop()!;
        if (activeScopes.has(scope)) continue;
        activeScopes.add(scope);
        for (const node of definitions[scope]?.nodes ?? []) if (node.nodeType === 'library' && typeof node.mechanicName === 'string') queue.push(node.mechanicName);
    }

    function unresolved(path: SourcePath, scope: string | null, message: string, writes = false) {
        index.unresolvedEffects.push({
            path,
            scope,
            reads: true,
            writes,
            active: scope === null || activeScopes.has(scope),
            message
        });
    }

    function expressionReference(id: string, scope: string | null, path: SourcePath) {
        const expressions = scope === null ? graph.expressions : definitions[scope]?.expressions;
        const parameters = index.libraryUses.filter(use => use.definition === scope).flatMap(use => object(use.source.node.parameters) ? Object.keys(use.source.node.parameters) : []);
        const statePaths: SourcePath[] = [], active = new Set<string>();
        let resolved = true;
        const follow = (id: string): void => {
            if (parameters.includes(id) && (!object(expressions) || !Object.hasOwn(expressions, id))) return;
            if (!object(expressions) || !Object.hasOwn(expressions, id) || active.has(id)) {
                resolved = false;
                return;
            }
            active.add(id);
            visitStateReferences(expressions[id], read => {
                if (read.local) return;
                if (read.kind === 'expression') follow(String(read.statePath[1]));
                else statePaths.push(read.statePath);
            }, () => {
                resolved = false;
            });
            active.delete(id);
        };
        follow(id);
        index.expressionReferences.push({id, scope, path, resolved, statePaths});
        if (!resolved) unresolved(path, scope, `Unresolved expression dependency: ${id}.`);
    }

    const pendingReferences: { id: string; scope: string | null; path: SourcePath }[] = [];

    function reads(value: unknown, path: SourcePath, scope: string | null) {
        visitStateReferences(value, read => {
            if (read.local) return;
            if (read.kind === 'expression') pendingReferences.push({
                id: String(read.statePath[1]),
                scope,
                path: read.path
            });
            else index.stateReads.push({path: read.path, statePath: read.statePath});
        }, (path, message) => unresolved(path, scope, message), path);
    }

    function scan(value: Record<string, unknown>, scope: string | null, prefix: SourcePath) {
        const nodes = value.nodes;
        if (!Array.isArray(nodes)) {
            index.issues.push({path: [...prefix, 'nodes'], message: 'This scope has no node array.'});
            return;
        }
        const ids = new Set<string>();
        nodes.forEach((node, position) => {
            const path = [...prefix, 'nodes', position];
            if (!object(node) || typeof node.id !== 'string' || typeof node.nodeType !== 'string') {
                index.issues.push({path, message: 'This node needs authored string identity and type.'});
                return;
            }
            if (ids.has(node.id)) index.issues.push({
                path,
                message: 'Duplicate node identity in this scope; editing needs an unambiguous source.'
            });
            ids.add(node.id);
            const source: NodeSource = {
                key: sourceKey(scope, node.id),
                scope,
                id: node.id,
                path,
                node: node as ModelNode
            };
            index.nodes.push(source);
            if (node.nodeType === 'library' && typeof node.mechanicName === 'string') index.libraryUses.push({
                source,
                definition: node.mechanicName,
                present: Object.hasOwn(definitions, node.mechanicName)
            });
            const write = (key: unknown) => {
                if (typeof key === 'string') index.stateWrites.push({source, key});
            };
            if (node.nodeType === 'draw') {
                write(node.stateWriteKey);
                if (!Array.isArray(node.drawWeights) || !node.drawWeights.length) {
                    write(node.boardStateKey ?? 'board');
                    write('rows');
                    write('cols');
                }
            } else if (['putState', 'data'].includes(node.nodeType)) write(node.stateKey);
            else if (node.nodeType === 'modifyState') write(node.outputKey ?? '__modified__');
            else if (node.nodeType === 'loop') {
                write(`__iter_${node.id}__`);
                write(`__wins_${node.id}__`);
            }
            if (node.nodeType === 'getState') index.stateReads.push({
                path,
                statePath: [typeof node.stateKey === 'string' ? node.stateKey : '__default__']
            });
            if (node.nodeType === 'metricsSink' && typeof node.winStateKey === 'string') index.stateReads.push({
                path,
                statePath: [node.winStateKey]
            });
            if (!['draw', 'getState', 'putState', 'data', 'modifyState', 'loop', 'branch', 'library', 'metricsSink', 'map'].includes(node.nodeType)) unresolved(path, scope, `Unresolved node effects: ${node.nodeType}.`, true);
            if (node.nodeType === 'map') {
                const ports = (direction: string) => object(node[direction]) && Object.values(node[direction]).some(port => object(port) && port.type === 'State');
                if (ports('inputs') || ports('outputs')) unresolved(path, scope, 'A plugin/Map State contract needs an explicit dependency proof.', !!ports('outputs'));
            }
            for (const field of ['expressionId', 'conditionId', 'stopConditionId', 'weightExpressionId']) if (typeof node[field] === 'string') pendingReferences.push({
                id: node[field],
                scope,
                path: [...path, field]
            });
            if (node.exitReason !== undefined) reads(node.exitReason, [...path, 'exitReason'], scope);
            for (const direction of ['inputs', 'outputs']) if (object(node[direction])) for (const [name, port] of Object.entries(node[direction])) {
                if (object(port) && port.defaultValue !== undefined) reads(port.defaultValue, [...path, direction, name, 'defaultValue'], scope);
            }
            // Legacy malformed inline parameters remain inspectable, but cannot prove native literal-call effects.
            if (node.nodeType === 'library' && object(node.parameters)) for (const [name, parameter] of Object.entries(node.parameters)) if (typeof parameter !== 'string') {
                reads(parameter, [...path, 'parameters', name], scope);
                unresolved([...path, 'parameters', name], scope, 'Native Library parameters require literal strings.');
            }
        });
        if (object(value.expressions)) for (const [id, expression] of Object.entries(value.expressions)) {
            const path = [...prefix, 'expressions', id];
            index.expressions.push({key: sourceKey(scope, id), scope, id, path, value: expression});
            reads(expression, path, scope);
        }
    }

    scan(graph, null, []);
    for (const [scope, definition] of Object.entries(definitions)) scan(definition, scope, ['mechanics', scope]);
    for (const use of index.libraryUses) if (!use.present) unresolved(use.source.path, use.source.scope, `Unavailable Library dependency: ${use.definition}.`, true);
    pendingReferences.forEach(reference => expressionReference(reference.id, reference.scope, reference.path));
    return index;
}

/** Resolve current locations by authored identity; an old array offset is never an edit target. */
export function resolveNodeSource(graph: ModelGraph, key: string): NodeSource {
    const matches = indexGraphSources(graph).nodes.filter(node => node.key === key);
    if (matches.length !== 1) throw new Error('The source is missing or ambiguous. Rebuild its binding before editing.');
    return matches[0];
}
