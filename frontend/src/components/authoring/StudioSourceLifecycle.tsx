import {useEffect, useMemo, useRef, useState} from 'react';
import {indexGraphSources, type NodeSource} from '../../lib/authoring/sourceIndex';
import {
    changeSourceOutcomes,
    definitionCopyPlan,
    type OutcomeInput,
    type SourceConnection,
    type SourceDefinitionInput
} from '../../lib/authoring/sourceLifecycle';
import {parseCsv} from '../../lib/weightCsv';
import {reelObject} from '../../lib/reelCommands';
import type {WeightBinding} from '../../lib/authoring/weightBindings';
import type {PayBinding} from '../../lib/authoring/payBindings';
import type {StudioContext} from './StudioShared';
import {replaceStudioPatch, studioCandidate, type StudioPatch} from '../../lib/authoring/studioEdits';

const message = (error: unknown) => error instanceof Error ? error.message : 'The draft could not be staged.';

export function SourceCreateButton({context}: { context: StudioContext }) {
    const [open, setOpen] = useState(false);
    return <>
        <button disabled={context.disabled} onClick={() => setOpen(true)}>Create source</button>
        {open && <CreateDialog context={context} close={() => setOpen(false)}/>}</>;
}

function CreateDialog({context, close}: { context: StudioContext; close: () => void }) {
    const [kind, setKind] = useState<'outcomes' | 'cells' | 'reels'>('outcomes'), [name, setName] = useState('my-source'), [stateKey, setStateKey] = useState('myRoll'), [rowsText, setRowsText] = useState('1,3,0\n5,1,0'), [dictionary, setDictionary] = useState('A\tSymbol A\nB\tSymbol B');
    const board = reelObject(context.graph.boardConfig), [rows, setRows] = useState(String(board?.rows ?? 3)), [columns, setColumns] = useState(String(board?.columns ?? 3)), [strips, setStrips] = useState('A,B,A\nB,A,B\nA,A,B');
    const [error, setError] = useState<string | null>(null), dialog = useRef<HTMLDialogElement>(null),
        index = useMemo(() => indexGraphSources(context.graph), [context.graph]);
    const blank = context.graph.nodes?.length === 1 && context.graph.nodes[0].nodeType === 'metricsSink' && context.graph.nodes[0].winStateKey === undefined && (!Array.isArray(context.graph.edges) || !context.graph.edges.length);
    const [connection, setConnection] = useState(blank ? 'empty-round' : 'definition');
    useEffect(() => {
        dialog.current?.showModal();
    }, []);
    const connections = index.nodes.filter(source => source.node.nodeType === 'library' && reelObject(reelObject(source.node.inputs)?.state)?.type === 'State' && reelObject(reelObject(source.node.outputs)?.state)?.type === 'State');
    const edges = [null, ...Object.keys(context.graph.mechanics ?? {})].flatMap(scope => {
        const graph = scope === null ? context.graph : context.graph.mechanics![scope],
            edges = Array.isArray(graph.edges) ? graph.edges as Record<string, unknown>[] : [];
        return edges.filter(edge => reelObject(reelObject(graph.nodes?.find(node => node.id === edge.sourceNodeId)?.outputs)?.[String(edge.sourcePort)])?.type === 'State' && reelObject(reelObject(graph.nodes?.find(node => node.id === edge.targetNodeId)?.inputs)?.[String(edge.targetPort)])?.type === 'State').map(edge => ({
            scope,
            edge,
            key: JSON.stringify(['edge', scope, edge.id])
        }));
    });
    const stage = () => {
        try {
            const outcomeRows = parseCsv(rowsText).map(row => {
                if (row.length !== 3) throw new Error('Each outcome row needs ID, weight and numeric payload.');
                return {id: row[0], weight: row[1], payload: row[2]};
            });
            const symbols = context.symbols.length ? undefined : parseCsv(dictionary, '\t').map(row => ({
                id: row[0],
                name: row[1],
                kind: row[2] || 'Standard'
            }));
            const input: SourceDefinitionInput = kind === 'outcomes' ? {
                kind,
                name,
                stateKey,
                outcomes: outcomeRows
            } : kind === 'cells' ? {
                kind,
                name,
                columns: Number(columns),
                rows: Number(rows),
                outcomes: outcomeRows,
                symbols
            } : {kind, name, rows: Number(rows), strips: parseCsv(strips), symbols};
            const parsed = connection === 'definition' || connection === 'empty-round' ? {kind: connection} as SourceConnection : connection.startsWith('use:') ? {
                kind: 'replace' as const,
                nodeKey: connection.slice(4)
            } : {
                kind: 'edge' as const,
                scope: JSON.parse(connection)[1] as string | null,
                edgeId: String(JSON.parse(connection)[2])
            };
            const patch: StudioPatch = {kind: 'create-source', key: name, input, connection: parsed},
                candidate = studioCandidate(context.graph, replaceStudioPatch(context.patches, patch));
            if (candidate.error) throw new Error(candidate.error);
            context.stage([patch]);
            close();
        } catch (error) {
            setError(message(error));
        }
    };
    return <dialog ref={dialog} className="studio-import-dialog studio-tabular-dialog studio-source-dialog"
                   onCancel={close}>
        <header>
            <div><h2>Create drawing source</h2><p>Compose ordinary graph primitives and choose exactly where this source
                runs.</p></div>
            <button aria-label="Close source creation" onClick={close}>×</button>
        </header>
        <div className="studio-import-mapping"><label>Source kind<select aria-label="New source kind" value={kind}
                                                                         onChange={event => {
                                                                             const next = event.target.value as typeof kind;
                                                                             setKind(next);
                                                                             const ids = context.symbols.length ? context.symbols.map(symbol => symbol.id) : ['A', 'B'];
                                                                             if (next === 'cells') setRowsText(ids.map(id => `${id},1,0`).join('\n'));
                                                                             if (next === 'reels') setStrips(Array.from({length: Number(columns) || 3}, () => ids.slice(0, 12).join(',')).join('\n'));
                                                                         }}>
            <option value="outcomes">Weighted outcomes</option>
            <option value="cells">Independent board cells</option>
            <option value="reels">Ordered physical reels</option>
        </select></label><label>Definition name<input aria-label="New source name" value={name}
                                                      onChange={event => setName(event.target.value)}/></label>{kind === 'outcomes' ?
            <label>New state key<input aria-label="New source state key" value={stateKey}
                                       onChange={event => setStateKey(event.target.value)}/></label> : <><label>Visible
                rows<input aria-label="New source rows" value={rows} inputMode="numeric"
                           onChange={event => setRows(event.target.value)}/></label>{kind === 'cells' &&
                <label>Columns<input aria-label="New source columns" value={columns} inputMode="numeric"
                                     onChange={event => setColumns(event.target.value)}/></label>}</>}</div>
        {kind === 'reels' ?
            <label>One ordered strip per line · comma-separated symbol IDs<textarea aria-label="New source strips"
                                                                                    value={strips}
                                                                                    onChange={event => setStrips(event.target.value)}
                                                                                    spellCheck={false}/></label> :
            <label>Outcome ID, exact weight, numeric payload · one row per line<textarea
                aria-label="New source outcomes" value={rowsText} onChange={event => setRowsText(event.target.value)}
                spellCheck={false}/></label>}
        {kind !== 'outcomes' && !context.symbols.length &&
            <label>New dictionary · ID, name, optional kind · tab-separated<textarea
                aria-label="New source symbol dictionary" value={dictionary}
                onChange={event => setDictionary(event.target.value)} spellCheck={false}/></label>}
        {kind !== 'outcomes' && context.symbols.length > 0 &&
            <p className="studio-import-policy-note">This source uses the project’s {context.symbols.length} dictionary
                symbols. Board geometry remains {String(board?.columns)} × {String(board?.rows)}; cell payloads must
                stay zero.</p>}
        <label>Consumer / connection<select aria-label="New source connection" value={connection}
                                            onChange={event => setConnection(event.target.value)}>
            <option value="definition">Definition only · connect later in Logic Graph</option>
            {blank && <option value="empty-round">Start this blank round · numeric outcome pays, boards have no payouts
                yet</option>}{connections.map(source => <option key={source.key}
                                                                value={`use:${source.key}`}>Replace {source.scope ?? 'Root'} / {String(source.node.label ?? source.id)}</option>)}{edges.map(({
                                                                                                                                                                                                  scope,
                                                                                                                                                                                                  edge,
                                                                                                                                                                                                  key
                                                                                                                                                                                              }) =>
            <option key={key}
                    value={key}>Before {scope ?? 'Root'} / {String(edge.targetNodeId)} from {String(edge.sourceNodeId)}</option>)}
        </select></label>
        <div className="studio-derived">
            <b>{connection === 'definition' ? 'The active round stays unchanged' : 'This adds or replaces a drawing action'}</b><span>{connection === 'definition' ? 'The definition is saved in this GraphConfig. Select a consumer later to execute it.' : 'The selected action/edge is part of the atomic edit. Added draws can change random consumption. Existing definitions and unrelated fields are retained.'}</span>
        </div>
        {error && <p className="studio-error" role="alert">{error}</p>}
        <footer className="studio-import-actions">
            <button onClick={close}>Cancel</button>
            <button className="studio-primary" disabled={context.disabled} onClick={stage}>Stage new source</button>
        </footer>
    </dialog>;
}

export function SourceCopyControls({context, uses}: { context: StudioContext; uses: NodeSource[] }) {
    const [selected, setSelected] = useState(uses[0]?.key ?? ''), [name, setName] = useState('private-source'), [open, setOpen] = useState(false), [error, setError] = useState<string | null>(null),
        dialog = useRef<HTMLDialogElement>(null);
    useEffect(() => {
        if (open) dialog.current?.showModal();
    }, [open]);
    const current = uses.find(use => use.key === selected) ?? uses[0];
    const plan = useMemo(() => {
        try {
            return {value: current ? definitionCopyPlan(context.graph, current.key) : null, error: null};
        } catch (error) {
            return {value: null, error: message(error)};
        }
    }, [context.graph, current]);
    const stage = () => {
        try {
            const patch: StudioPatch = {kind: 'copy-source', key: current.key, nodeKey: current.key, name};
            const candidate = studioCandidate(context.graph, replaceStudioPatch(context.patches, patch));
            if (candidate.error) throw new Error(candidate.error);
            context.stage([patch]);
            setOpen(false);
            setError(null);
        } catch (error) {
            setError(message(error));
        }
    };
    if (!uses.length) return null;
    return <div className="studio-source-sharing">
        <span>{uses.length} known uses · definition edits affect every caller</span>
        <button disabled={context.disabled} onClick={() => setOpen(true)}>Make one use independent</button>
        {open && <dialog ref={dialog} className="studio-import-dialog studio-tabular-dialog studio-source-dialog"
                         onCancel={() => setOpen(false)}>
            <header>
                <div><h2>Make one source use independent</h2><p>Retain its graph logic and isolate future definition
                    edits.</p></div>
                <button aria-label="Close independent source" onClick={() => setOpen(false)}>×</button>
            </header>
            <label>Selected Library use<select aria-label="Independent source use" value={current?.key ?? ''}
                                               onChange={event => setSelected(event.target.value)}>{uses.map(use =>
                <option key={use.key}
                        value={use.key}>{use.scope ?? 'Root'} / {String(use.node.label ?? use.id)}</option>)}</select></label><label>Private
            definition name<input aria-label="Independent source name" value={name}
                                  onChange={event => setName(event.target.value)}/></label>{plan.value &&
            <div className="studio-derived"><b>{plan.value.copied.length} definitions
                · {plan.value.privateStrips.length} private strip arrays
                · {plan.value.privateConditionFields.length} private condition fields</b><span>Only the selected Library reference is redirected. Other callers keep their current definitions. Game state continues to follow the existing consumer contract.</span><span>Apply this copy, then select the new source to edit its weights or strips independently.</span>
            </div>}{(plan.error || error) && <p className="studio-error" role="alert">{plan.error ?? error}</p>}
            <footer className="studio-import-actions">
                <button onClick={() => setOpen(false)}>Cancel</button>
                <button className="studio-primary" disabled={!plan.value || context.disabled} onClick={stage}>Stage
                    independent copy
                </button>
            </footer>
        </dialog>}</div>;
}

export function OutcomeDomainEditor({context, source}: { context: StudioContext; source: WeightBinding }) {
    const domainError = useMemo(() => {
        try {
            changeSourceOutcomes(context.graph, source.key, source.rows.map(row => ({
                id: row.outcomeId,
                weight: String(row.weight),
                payload: String(row.value)
            })));
            return null;
        } catch (error) {
            return message(error);
        }
    }, [context.graph, source]);
    const patch = context.patches.find(patch => patch.kind === 'outcomes' && patch.key === source.key),
        weighted = context.patches.find(patch => patch.kind === 'weights' && patch.key === source.key);
    const rows: OutcomeInput[] = patch?.kind === 'outcomes' ? patch.rows : source.rows.map(row => ({
        id: row.outcomeId,
        weight: weighted?.kind === 'weights' ? weighted.values.find(([id]) => id === row.outcomeId)?.[1] ?? String(row.weight) : String(row.weight),
        payload: String(row.value)
    }));
    const [newId, setNewId] = useState(''), [newWeight, setNewWeight] = useState('1');
    const update = (next: OutcomeInput[]) => context.stage([{kind: 'outcomes', key: source.key, rows: next}]);
    if (source.capability !== 'editable') return null;
    if (domainError) return <div className="studio-derived"><b>Outcome domain belongs to its consumer
        contract</b><span>{domainError}</span><span>Its supported weight inputs remain editable above. Use the matching payout domain or the source graph to change dependent outcomes.</span>
    </div>;
    return <details className="studio-outcome-domain">
        <summary>Add / remove supported outcomes · {rows.length} rows</summary>
        <p>Existing IDs stay stable. Board symbols must exist in this dictionary; custom/prize/control dependencies
            reject unsupported changes before applying.</p>
        <div className="studio-matrix-scroll">
            <table className="studio-matrix">
                <thead>
                <tr>
                    <th>Outcome ID</th>
                    <th>Weight</th>
                    <th>Numeric payload</th>
                    <th>Remove</th>
                </tr>
                </thead>
                <tbody>{rows.map((row, index) => <tr key={row.id}>
                    <th>{row.id}</th>
                    <td><input aria-label={`Domain weight ${row.id}`} value={row.weight} disabled={context.disabled}
                               onChange={event => update(rows.map((row, i) => i === index ? {
                                   ...row,
                                   weight: event.target.value
                               } : row))}/></td>
                    <td><input aria-label={`Domain payload ${row.id}`} value={row.payload} disabled={context.disabled}
                               onChange={event => update(rows.map((row, i) => i === index ? {
                                   ...row,
                                   payload: event.target.value
                               } : row))}/></td>
                    <td>
                        <button aria-label={`Remove outcome ${row.id}`} disabled={context.disabled || rows.length === 1}
                                onClick={() => update(rows.filter((_, i) => i !== index))}>Remove
                        </button>
                    </td>
                </tr>)}</tbody>
            </table>
        </div>
        <div className="studio-domain-add"><label>New outcome ID<input aria-label="New outcome ID" value={newId}
                                                                       onChange={event => setNewId(event.target.value)}/></label><label>Exact
            added weight<input aria-label="New outcome weight" value={newWeight}
                               onChange={event => setNewWeight(event.target.value)}/></label>
            <button disabled={context.disabled || !newId || rows.some(row => row.id === newId)} onClick={() => {
                update([...rows, {id: newId, weight: newWeight, payload: '0'}]);
                setNewId('');
            }}>Add outcome
            </button>
        </div>
        <p>Removing an outcome discards that outcome’s weight for this Draw only. Apply reconciles the complete
            candidate; unsupported dependencies leave the graph intact.</p></details>;
}

export function PrizeDomainEditor({context, binding}: { context: StudioContext; binding: PayBinding }) {
    const patch = context.patches.find(patch => patch.kind === 'prize-domain' && patch.key === binding.key),
        values = patch?.kind === 'prize-domain' ? patch.values : binding.rows[0].values.map(value => ({value} as {
            value: string;
            addedWeight?: string
        }));
    const [value, setValue] = useState(''), [weight, setWeight] = useState('1'),
        discard = patch?.kind === 'prize-domain' && patch.discard;
    const pendingValues = context.patches.some(patch => patch.kind === 'payout' && patch.key === binding.key),
        disabled = context.disabled || pendingValues;
    const stage = (next: typeof values, remove = discard) => context.stage([{
        kind: 'prize-domain',
        key: binding.key,
        values: next,
        discard: remove
    }]);
    return <details className="studio-outcome-domain">
        <summary>Add / remove numeric prizes · {values.length} outcomes</summary>
        <p>{pendingValues && <b>Apply or discard pending prize-value edits before changing this domain. </b>}Retained
            prizes keep each Draw’s independent weight. Added prizes require an exact weight; removal requires
            discarding their old weights.</p>
        <div className="studio-prize-domain-list">{values.map((row, index) => <div key={index}>
            <b>× {row.value}</b><span>{row.addedWeight === undefined ? 'Retained independently per Draw' : `New weight ${row.addedWeight} per Draw`}</span>
            <button aria-label={`Remove prize ${row.value}`} disabled={disabled || values.length === 1}
                    onClick={() => stage(values.filter((_, i) => i !== index))}>Remove prize
            </button>
        </div>)}</div>
        <div className="studio-domain-add"><label>New prize · stake multiples<input aria-label="New prize value"
                                                                                    value={value}
                                                                                    onChange={event => setValue(event.target.value)}/></label><label>Exact
            weight for every Draw<input aria-label="New prize weight" value={weight}
                                        onChange={event => setWeight(event.target.value)}/></label>
            <button disabled={disabled || !value || values.some(row => row.value === value)} onClick={() => {
                stage([...values, {value, addedWeight: weight}]);
                setValue('');
            }}>Add prize
            </button>
        </div>
        <label className="studio-check"><input type="checkbox" checked={!!discard} disabled={disabled}
                                               onChange={event => stage(values, event.target.checked)}/>Discard weights
            of removed prize outcomes</label><p>All verified prize consumers and exact payout declarations change
        together. Existing round caps stay intact.</p></details>;
}
