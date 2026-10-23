import {useEffect, useRef, useState} from 'react';
import type {components} from '../../api/generated-types';
import {useAppStore} from '../../store';
import {canonicalJson} from '../../lib/hash';
import {graphRequest} from '../../lib/graphApi';
import {projectContentScope} from '../../lib/projectFiles';
import {useModelProjection} from '../../hooks/useModelProjection';
import type {ModelGraph} from '../../lib/modelProjection';
import {
    commitGraphEdit,
    currentGraphUndo,
    prepareGraphEdit,
    undoGraphEdit,
    type GraphEdit
} from '../../lib/authoring/transactions';
import GraphDialog from './GraphDialog';

type Report = components['schemas']['GraphSimplificationResponse'];
const RULE_LABELS: Record<string, string> = {
    'exact-constant': 'Calculate exact constants',
    'constant-branch': 'Remove an unreachable expression branch',
    'short-circuit': 'Remove a skipped Boolean operand',
    'boolean-identity': 'Simplify a Boolean identity',
    'numeric-identity': 'Simplify a numeric identity',
    'double-negation': 'Remove double negation',
    'dominated-predicate': 'Reuse an established condition',
};

export default function GraphSimplification() {
    const projectId = useAppStore(state => state.projectId);
    const [open, setOpen] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('');
    const [preview, setPreview] = useState<{ report: Report; edit: GraphEdit } | null>(null);
    const pending = useRef<AbortController | null>(null);
    const graph = useModelProjection();
    const undo = graph && projectId ? currentGraphUndo(projectId, graph) : null;
    const stale = !!preview && (projectId !== preview.edit.projectId || projectContentScope() !== preview.edit.origin
        || canonicalJson(graph) !== canonicalJson(preview.edit.base));

    useEffect(() => () => pending.current?.abort(), []);
    const close = () => {
        pending.current?.abort();
        setOpen(false);
        setBusy(false);
        setPreview(null);
        setError('');
    };
    const analyze = async () => {
        pending.current?.abort();
        const controller = new AbortController();
        pending.current = controller;
        setOpen(true);
        setBusy(true);
        setPreview(null);
        setError('');
        try {
            const edit = prepareGraphEdit('Simplify graph', base => base);
            const report = await graphRequest<Report>('validate/simplify', {config: edit.base}, controller.signal);
            controller.signal.throwIfAborted();
            if (!report.config || !Array.isArray(report.changes) || !Array.isArray(report.retained)) throw new Error('No simplification report returned.');
            const candidate = report.config as ModelGraph;
            setPreview({
                report,
                edit: {...edit, candidate, changed: canonicalJson(edit.base) !== canonicalJson(candidate)}
            });
        } catch (reason) {
            if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
        } finally {
            if (!controller.signal.aborted) setBusy(false);
        }
    };
    const apply = async () => {
        if (!preview || stale) return;
        const controller = new AbortController();
        pending.current = controller;
        setBusy(true);
        setError('');
        try {
            await commitGraphEdit(preview.edit, controller.signal);
            if (!controller.signal.aborted) close();
        } catch (reason) {
            if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
        } finally {
            if (!controller.signal.aborted) setBusy(false);
        }
    };
    const changes = preview?.report.changes ?? [];
    const before = changes.reduce((sum, change) => sum + change.beforeCost, 0);
    const after = changes.reduce((sum, change) => sum + change.afterCost, 0);
    const rules = new Map<string, number>();
    for (const change of changes) for (const [rule, count] of Object.entries(change.rules)) rules.set(rule, (rules.get(rule) ?? 0) + count);

    return <>
        <button className="btn" disabled={!projectId || !graph || busy} onClick={() => void analyze()}>Simplify graph
        </button>
        {undo?.edit.label === 'Simplify graph' && <button className="btn" onClick={() => {
            try {
                undoGraphEdit(undo);
                setError('');
            } catch (reason) {
                setOpen(true);
                setError(String(reason));
            }
        }}>Undo simplification</button>}
        {open && <GraphDialog title="Simplify graph" onClose={close}>
            {busy && <p role="status">Checking safe simplifications…</p>}
            {error && <p role="alert">{error}</p>}
            {preview && <>
                <p>{changes.length ? `${changes.length} expression${changes.length === 1 ? '' : 's'} can be simplified.` : 'No proven simplifications found in the editable source.'}</p>
                {changes.length > 0 && <>
                    <p>Static expression cost: {before.toLocaleString()} → {after.toLocaleString()}. This estimate is
                        not a measured spin rate.</p>
                    <ul>{[...rules].map(([rule, count]) => <li
                        key={rule}>{RULE_LABELS[rule] ?? rule}: {count}</li>)}</ul>
                    <details>
                        <summary>Changed expressions</summary>
                        <ul>{changes.map(change => <li key={change.path}><code
                            style={{overflowWrap: 'anywhere'}}>{change.path}</code> · {change.beforeCost} → {change.afterCost}
                        </li>)}</ul>
                    </details>
                    <p>Draw order, state writes, observation points and shared subgraph definitions are preserved. Apply
                        updates the complete model; one undo restores the original.</p>
                </>}
                {preview.report.budgetReached &&
                    <p>The analysis limit was reached. The report includes only completed, proven rewrites.</p>}
                {preview.report.retained.length > 0 && <details>
                    <summary>{preview.report.retained.length} expressions retained</summary>
                    <ul>{preview.report.retained.map(item => <li key={item}
                                                                 style={{overflowWrap: 'anywhere'}}>{item}</li>)}</ul>
                </details>}
                {stale &&
                    <p role="alert">The project changed. Close this preview and analyze the current model again.</p>}
                <button className="btn primary" disabled={busy || stale || !preview.edit.changed}
                        onClick={() => void apply()}>Apply simplification
                </button>
            </>}
        </GraphDialog>}
    </>;
}
