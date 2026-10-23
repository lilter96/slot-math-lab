import {useEffect, useRef, useState} from 'react';
import {useAppStore} from '../../store';
import {useModelProjection} from '../../hooks/useModelProjection';
import {canonicalJson} from '../../lib/hash';
import {projectContentScope} from '../../lib/projectFiles';
import {collectionRepeatCalls, repeatCollection, type CollectionRepeatSpec} from '../../lib/authoring/collectionRepeat';
import {
    commitGraphEdit,
    currentGraphUndo,
    prepareGraphEdit,
    undoGraphEdit,
    type GraphEdit
} from '../../lib/authoring/transactions';
import GraphDialog from './GraphDialog';
import './CollectionRepeat.css';

const label = 'Repeat over collection';
export default function CollectionRepeat() {
    const graph = useModelProjection();
    const projectId = useAppStore(state => state.projectId);
    const selected = useAppStore(state => state.selectedNodeId);
    const trail = useAppStore(state => state.graphTrail);
    const calls = graph ? collectionRepeatCalls(graph) : [];
    const scope = trail.at(-1)?.mechanic ?? null;
    const localCalls = calls.filter(call => call.scope === scope);
    const [open, setOpen] = useState(false), [error, setError] = useState(''), [busy, setBusy] = useState(false);
    const [spec, setSpec] = useState<CollectionRepeatSpec | null>(null), [preview, setPreview] = useState<GraphEdit | null>(null);
    const pending = useRef<AbortController | null>(null);
    useEffect(() => () => pending.current?.abort(), []);
    const undo = graph && projectId ? currentGraphUndo(projectId, graph) : null;
    const stale = !!preview && (projectId !== preview.projectId || projectContentScope() !== preview.origin || canonicalJson(graph) !== canonicalJson(preview.base));
    const close = () => {
        pending.current?.abort();
        setOpen(false);
        setBusy(false);
        setPreview(null);
        setError('');
    };
    const start = () => {
        const call = localCalls.find(call => call.id === selected) ?? localCalls[0];
        setSpec({
            call: call?.key ?? '',
            name: `each-${crypto.randomUUID()}`,
            sourceKey: '',
            itemKey: 'currentItem',
            indexKey: 'currentIndex',
            itemType: 'Number',
            maxItems: 64
        });
        setPreview(null);
        setError('');
        setOpen(true);
    };
    const change = (patch: Partial<CollectionRepeatSpec>) => {
        setSpec(current => current && ({...current, ...patch}));
        setPreview(null);
        setError('');
    };
    const analyze = () => {
        try {
            if (!spec) return;
            setPreview(prepareGraphEdit(label, base => repeatCollection(base, spec)));
            setError('');
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : String(reason));
        }
    };
    const apply = async () => {
        if (!preview || stale) return;
        const controller = new AbortController();
        pending.current = controller;
        setBusy(true);
        setError('');
        try {
            await commitGraphEdit(preview, controller.signal);
            if (!controller.signal.aborted) close();
        } catch (reason) {
            if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
        } finally {
            if (!controller.signal.aborted) setBusy(false);
        }
    };
    return <>
        <button className="btn" disabled={!graph || !projectId || busy} onClick={start}>{label}</button>
        {undo?.edit.label === label && <button className="btn" onClick={() => {
            try {
                undoGraphEdit(undo);
                setError('');
            } catch (reason) {
                setError(String(reason));
                setOpen(true);
            }
        }}>Undo collection repeat</button>}
        {open && <GraphDialog title={label} onClose={close}>
            <div className="collection-repeat">
                <p>Run one editable subgraph for each item, in collection order.</p>
                {!localCalls.length &&
                    <p role="status">Add or open a subgraph call with one State input and output, then return here.</p>}
                {spec && <fieldset disabled={busy || !localCalls.length}>
                    <legend>Collection and body</legend>
                    <label>Subgraph call<select value={spec.call}
                                                onChange={event => change({call: event.target.value})}>
                        <option value="">Choose a call</option>
                        {localCalls.map(call => <option value={call.key}
                                                        key={call.key}>{call.node.label ?? call.id} · {call.node.mechanicName}</option>)}
                    </select></label>
                    <label>Collection state key<input value={spec.sourceKey}
                                                      onChange={event => change({sourceKey: event.target.value})}
                                                      autoComplete="off" placeholder="For example, linePaths"/></label>
                    <div className="collection-repeat-grid">
                        <label>Current item state key<input value={spec.itemKey}
                                                            onChange={event => change({itemKey: event.target.value})}
                                                            autoComplete="off"/></label>
                        <label>Current index state key<input value={spec.indexKey}
                                                             onChange={event => change({indexKey: event.target.value})}
                                                             autoComplete="off"/></label>
                        <label>Collection item type<select value={spec.itemType}
                                                           onChange={event => change({itemType: event.target.value as CollectionRepeatSpec['itemType']})}>
                            {['Number', 'String', 'Boolean', 'Array', 'Record'].map(type => <option
                                key={type}>{type}</option>)}
                        </select></label>
                        <label>Maximum items<input type="number" min="0" max="4096" step="1"
                                                   value={Number.isFinite(spec.maxItems) ? spec.maxItems : ''}
                                                   onChange={event => change({maxItems: event.target.value === '' ? NaN : Number(event.target.value)})}/></label>
                    </div>
                    <p className="collection-repeat-note">The body reads the current item and index from these keys. Use
                        distinct keys for nested repeats. Existing values are replaced; an empty collection leaves them
                        unchanged.</p>
                    <button type="button" className="btn" onClick={analyze}>Preview collection repeat</button>
                </fieldset>}
                {error && <p role="alert">{error}</p>}
                {preview && spec && <section aria-label="Collection repeat preview">
                    <h3>One body, up to {spec.maxItems} items</h3>
                    <ol>
                        <li>Snapshot <code>{spec.sourceKey}</code> and check its bound.</li>
                        <li>Bind <code>{spec.itemKey}</code> and <code>{spec.indexKey}</code>, then run the body once
                            per item.
                        </li>
                        <li>Continue from the original call’s output.</li>
                    </ol>
                    <p>Body parameters and shared definitions stay editable. This replaces this call in the current
                        definition; all uses of that definition see the change. Changing the source collection inside
                        the body leaves the remaining snapshot intact.</p>
                    <details>
                        <summary>Inspect the generated graph</summary>
                        <pre>{JSON.stringify(preview.candidate.mechanics?.[spec.name], null, 2)}</pre>
                    </details>
                    <p>Apply validates the complete model. One undo restores the model and open subgraph. Spin
                        throughput must be measured for your game.</p>
                    {stale && <p role="alert">The project changed. Preview the current model again before applying.</p>}
                    <button type="button" className="btn primary" disabled={busy || stale}
                            onClick={() => void apply()}>{busy ? 'Validating model…' : 'Apply collection repeat'}</button>
                </section>}
            </div>
        </GraphDialog>}
    </>;
}
