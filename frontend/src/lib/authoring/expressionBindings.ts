import type {Expr} from '../../games/shared/graph/expr';

export type ExpressionPath = readonly (string | number)[];

export interface StateReference {
    expression: Record<string, unknown>;
    path: ExpressionPath;
    statePath: (string | number)[];
    kind: 'field' | 'collection' | 'expression';
    local: boolean;
}

const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
export const stateNamespace = (target: unknown) => target === undefined || target === null || target === 'state' || target === 'board';
const expressionTypes = new Set(['constant', 'fieldAccess', 'binary', 'compare', 'if', 'aggregate', 'not', 'call', 'bitset', 'fold', 'map', 'filter', 'reachable']);

/** Existing AST lexical scopes, shared by source inventory and private-field rewriting. No evaluation. */
export function visitStateReferences(value: unknown, reference: (read: StateReference) => void,
                                     issue: (path: ExpressionPath, message: string) => void = () => {
                                     }, path: ExpressionPath = [], locals: ReadonlySet<string> = new Set()): void {
    if (Array.isArray(value)) {
        value.forEach((child, index) => visitStateReferences(child, reference, issue, [...path, index], locals));
        return;
    }
    if (!object(value)) return;
    const type = value.exprType;
    if (typeof type === 'string' && !expressionTypes.has(type)) issue(path, `Unresolved expression form: ${type}.`);
    if (type === 'fieldAccess') {
        if (stateNamespace(value.target)) {
            const parts = value.path;
            if (!Array.isArray(parts) || !parts.length || !parts.every(part => typeof part === 'string' || Number.isSafeInteger(part))) issue(path, 'Unresolved state field path.');
            else reference({
                expression: value, path, statePath: [...parts], local: locals.has(String(parts[0])),
                kind: parts[0] === 'expressions' && parts.length > 1 ? 'expression' : 'field'
            });
        } else if (value.target !== 'measurement') issue(path, `Unresolved field namespace: ${String(value.target)}.`);
    }
    const collection = ['aggregate', 'map', 'filter', 'fold'].includes(String(type));
    const nested = new Set(locals);
    if (collection) {
        if (typeof value.stateKey !== 'string') issue(path, 'A collection needs an authored StateKey.');
        else reference({
            expression: value,
            path,
            statePath: [value.stateKey],
            kind: 'collection',
            local: locals.has(value.stateKey)
        });
        const item = type === 'aggregate' && value.itemName === undefined ? 'item' : value.itemName;
        if (typeof item === 'string') nested.add(item);
        else issue(path, 'A collection needs its runtime item binding.');
        if (type !== 'aggregate' && typeof value.indexName === 'string') nested.add(value.indexName);
        if (type === 'fold') {
            if (typeof value.accName === 'string') nested.add(value.accName);
            else issue(path, 'A fold needs its runtime accumulator binding.');
        }
    }
    const bodyKeys = type === 'aggregate' ? ['predicate', 'valueExpr'] : type === 'filter' ? ['predicate'] : ['body'];
    const children: Record<string, string[]> = {
        constant: [],
        fieldAccess: [],
        binary: ['left', 'right'],
        compare: ['left', 'right'],
        if: ['condition', 'thenExpr', 'elseExpr'],
        aggregate: ['predicate', 'valueExpr'],
        not: ['expr'],
        call: ['args'],
        bitset: ['args'],
        reachable: ['adjacency', 'allowed', 'seeds'],
        fold: ['init', 'body'],
        map: ['body'],
        filter: ['predicate']
    };
    const keys = typeof type === 'string' && Object.hasOwn(children, type) ? children[type] : Object.keys(value);
    for (const key of keys) {
        const child = value[key];
        if (!child || typeof child !== 'object') continue;
        visitStateReferences(child, reference, issue, [...path, key], collection && bodyKeys.includes(key) ? nested : locals);
    }
}

export function bindStateExpression(expression: Expr, names: ReadonlyMap<string, string>, reads?: Set<string>): Expr {
    const result = structuredClone(expression);
    visitStateReferences(result, reference => {
        if (reference.kind !== 'collection') reference.expression.target = 'state';
        if (reference.local || reference.kind === 'expression') return;
        const key = String(reference.statePath[0]), bound = names.get(key) ?? key;
        if (reference.kind === 'collection') reference.expression.stateKey = bound;
        else {
            (reference.expression.path as unknown[])[0] = bound;
            // EvalFieldAccess aliases all read the same state. Canonical state also satisfies
            // the existing compiler's stricter board-field schema for renamed private keys.
            reference.expression.target = 'state';
        }
        reads?.add(bound);
    }, (_path, message) => {
        throw new Error(message);
    });
    return result;
}
