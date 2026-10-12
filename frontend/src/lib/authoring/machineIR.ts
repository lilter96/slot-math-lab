import type {Expr} from '../../games/shared/graph/expr';

export type MachineValue = string | number | boolean;

export interface MachineVariable {
    key: string;
    scope: 'private' | 'shared';
    initial: MachineValue
}

export type MachineAction = { id: string; label: string; kind: 'assign'; key: string; expression: Expr }
    | {
    id: string;
    label: string;
    kind: 'draw';
    key: string;
    outcomes: { id: string; weight: number; payload: number }[]
}
    | {
    id: string;
    label: string;
    kind: 'library';
    definition: string;
    parameters: Record<string, string>;
    parameterBindings?: Record<string, string>
};

export interface MachineTransition {
    id: string;
    label: string;
    target: string;
    guard: Expr | null;
    effects: MachineAction[]
}

export interface MachineState {
    id: string;
    label: string;
    terminal: boolean;
    actions: MachineAction[];
    transitions: MachineTransition[]
}

/** Recoverable authoring input only. Once applied, ordinary GraphConfig carries every executable value. */
export interface MachineIR {
    version: 1;
    instance: string;
    name: string;
    entry: string;
    stepBudget: number;
    variables: MachineVariable[];
    reads?: string[];
    states: MachineState[]
}

export const machineDone = '__Done__';

/** Recovery admission checks structure, while semantic errors remain recoverable pending inputs. */
export function isMachineInput(value: unknown): value is MachineIR {
    const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
    const ast = (value: unknown) => object(value) && typeof value.exprType === 'string';
    const strings = (value: unknown) => object(value) && Object.values(value).every(value => typeof value === 'string');
    const action = (value: unknown): boolean => object(value) && typeof value.id === 'string' && typeof value.label === 'string' && (value.kind === 'library' ? typeof value.definition === 'string' && strings(value.parameters) && (value.parameterBindings === undefined || strings(value.parameterBindings)) : value.kind === 'assign' ? typeof value.key === 'string' && ast(value.expression) : value.kind === 'draw' && typeof value.key === 'string' && Array.isArray(value.outcomes) && value.outcomes.every(row => object(row) && typeof row.id === 'string' && typeof row.weight === 'number' && typeof row.payload === 'number'));
    return object(value) && value.version === 1 && typeof value.instance === 'string' && typeof value.name === 'string' && typeof value.entry === 'string' && typeof value.stepBudget === 'number'
        && (value.reads === undefined || Array.isArray(value.reads) && value.reads.every(key => typeof key === 'string'))
        && Array.isArray(value.variables) && value.variables.every(variable => object(variable) && typeof variable.key === 'string' && ['private', 'shared'].includes(String(variable.scope)) && ['string', 'number', 'boolean'].includes(typeof variable.initial))
        && Array.isArray(value.states) && value.states.every(state => object(state) && typeof state.id === 'string' && typeof state.label === 'string' && typeof state.terminal === 'boolean' && Array.isArray(state.actions) && state.actions.every(action) && Array.isArray(state.transitions) && state.transitions.every(transition => object(transition) && typeof transition.id === 'string' && typeof transition.label === 'string' && typeof transition.target === 'string' && (transition.guard === null || ast(transition.guard)) && Array.isArray(transition.effects) && transition.effects.every(action)));
}

export function validateMachine(machine: MachineIR): string[] {
    const errors: string[] = [], fail = (text: string) => errors.push(text),
        states = new Set(machine.states.map(state => state.id));
    if (machine.version !== 1 || !/^[A-Za-z][A-Za-z0-9_-]{0,79}$/.test(machine.instance) || !machine.name.trim()) fail('Choose a stable instance ID starting with a letter and a display name.');
    if (!Number.isSafeInteger(machine.stepBudget) || machine.stepBudget < 1 || machine.stepBudget > 100000) fail('The budget counts state visits, including terminal visits: use 1–100000.');
    if (!machine.states.length || machine.states.length > 64 || states.size !== machine.states.length || machine.states.some(state => !state.id || state.id === machineDone || !state.label.trim())) fail('Use 1–64 distinct named states; __Done__ is reserved.');
    if (!states.has(machine.entry)) fail('Select an existing starting state.');
    const variables = new Map(machine.variables.map(variable => [variable.key, variable]));
    if (machine.reads && (new Set(machine.reads).size !== machine.reads.length || machine.reads.some(key => !key))) fail('Read-only game fields need distinct non-empty keys.');
    if (variables.size !== machine.variables.length || machine.variables.some(variable => !variable.key || !['private', 'shared'].includes(variable.scope) || !['string', 'number', 'boolean'].includes(typeof variable.initial) || (typeof variable.initial === 'number' && (!Number.isFinite(variable.initial) || Math.abs(variable.initial) > Number.MAX_SAFE_INTEGER || variable.scope === 'private' && !Number.isSafeInteger(variable.initial))))) fail('Variables need distinct keys, explicit private/shared scope and exact primitive initial values.');
    const actions = (values: MachineAction[], location: string) => {
        if (new Set(values.map(action => action.id)).size !== values.length || values.some(action => !action.id || !action.label.trim())) fail(`${location}: actions need distinct IDs and labels.`);
        for (const action of values) {
            if (action.kind === 'library') {
                if (!action.definition) fail(`${location}: select a Library operation.`);
                for (const key of Object.values(action.parameterBindings ?? {})) if (!variables.has(key) && !machine.reads?.includes(key)) fail(`${location}: declare the variable or read-only field ${key} before binding a Library parameter.`);
                continue;
            }
            if (!variables.has(action.key)) fail(`${location}: declare the variable ${action.key} and its shared/private scope.`);
            if (action.kind === 'draw') {
                if (variables.get(action.key) && typeof variables.get(action.key)!.initial !== 'string') fail(`${location}: Draw outcome fields have String initial values; payload and ID are separate.`);
                if (!action.outcomes.length || new Set(action.outcomes.map(outcome => outcome.id)).size !== action.outcomes.length || action.outcomes.some(outcome => !outcome.id || !Number.isSafeInteger(outcome.weight) || outcome.weight < 0 || !Number.isSafeInteger(outcome.payload)) || !action.outcomes.some(outcome => outcome.weight > 0)) fail(`${location}: use distinct outcomes, exact non-negative weights and a positive total.`);
            }
        }
    };
    for (const state of machine.states) {
        actions(state.actions, state.label);
        if (state.terminal && state.transitions.length) fail(`${state.label}: terminal states have no outgoing transitions.`);
        if (!state.terminal && (!state.transitions.length || state.transitions.at(-1)?.guard !== null || state.transitions.slice(0, -1).some(transition => transition.guard === null))) fail(`${state.label}: add exactly one unconditional fallback last. No matching guard must not silently complete a feature.`);
        if (new Set(state.transitions.map(transition => transition.id)).size !== state.transitions.length) fail(`${state.label}: transition IDs must be distinct.`);
        for (const transition of state.transitions) {
            if (!transition.id || !states.has(transition.target)) fail(`${state.label}: transition ${transition.id} needs an existing target and stable ID.`);
            actions(transition.effects, `${state.label} / ${transition.label}`);
        }
    }
    const reached = new Set<string>(), visit = (id: string) => {
        if (reached.has(id)) return;
        reached.add(id);
        machine.states.find(state => state.id === id)?.transitions.forEach(transition => visit(transition.target));
    };
    visit(machine.entry);
    for (const state of machine.states) if (!reached.has(state.id)) fail(`${state.label}: unreachable from the selected start. Reachability does not prove eventual termination.`);
    return errors;
}
