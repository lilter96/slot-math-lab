import {useEffect, useMemo, useRef, useState} from 'react';
import {canonicalJson} from '../../lib/hash';
import {
    type InputWorkbook,
    parseTableText,
    readSpreadsheet,
    tableText,
    writeSpreadsheet
} from '../../lib/authoring/spreadsheetIO';
import {
    autoColumns,
    type ColumnMapping,
    exportTable,
    type ImportOptions,
    type ImportPreview,
    nativeUnit,
    previewTable,
    type TableTarget,
    targetFields
} from '../../lib/authoring/studioTabular';
import {downloadText} from '../../lib/authoring/studioPresentation';
import type {StudioContext} from './StudioShared';

function downloadWorkbook(bytes: Uint8Array, name: string) {
    const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)], {type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'})),
        link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

export function StudioTableTools({context, target, onImport}: {
    context: StudioContext;
    target: TableTarget;
    onImport: () => void
}) {
    const [format, setFormat] = useState<'xlsx' | 'csv' | 'tsv'>('xlsx'), [busy, setBusy] = useState(false), [error, setError] = useState<string | null>(null),
        alive = useRef(true);
    useEffect(() => {
        alive.current = true;
        return () => {
            alive.current = false;
        };
    }, []);
    const reason = target.kind === 'weights' && target.bindings.find(source => source.capability !== 'editable')?.reason;
    const exported = async () => {
        setBusy(true);
        setError(null);
        try {
            const rows = exportTable(context.graph, target), file = `slotmath-${target.kind}.${format}`;
            if (format === 'xlsx') {
                const bytes = await writeSpreadsheet(rows, 'Inputs', [['Model', String(context.graph.name ?? 'Untitled model')], ['Source', target.label], ['Data', 'Applied graph inputs; pending edits are not exported.'], ['Units', target.kind === 'paytable' ? nativeUnit(target.binding) : target.kind === 'weights' ? 'Exact integer weights' : target.kind === 'reels' ? 'Reel starts at 1; physical index starts at 0; retained IDs/weights stay paired.' : 'Line starts at 1; columns and rows start at 0.'], ['Reimport', 'Select Inputs, map columns, preview exact values, then validate and apply.'], ['Literal cells', 'All exported cells are text, including numeric IDs, decimal amounts and fractions.']]);
                if (alive.current) downloadWorkbook(bytes, file);
            } else downloadText(file, tableText(rows, format), format === 'csv' ? 'text/csv' : 'text/tab-separated-values');
        } catch (failure) {
            if (alive.current) setError(failure instanceof Error ? failure.message : 'Export failed.');
        } finally {
            if (alive.current) setBusy(false);
        }
    };
    const copy = async () => {
        try {
            await navigator.clipboard.writeText(tableText(exportTable(context.graph, target), 'tsv'));
            setError(null);
        } catch {
            setError('Clipboard permission is unavailable. Download TSV or copy from the import text field.');
        }
    };
    return <div className="studio-table-tools">
        <div className="studio-actions">
            <button disabled={context.disabled || !!reason} onClick={onImport}>Import table</button>
            <button disabled={!!reason} onClick={() => void copy()}>Copy TSV</button>
        </div>
        <div className="studio-actions"><label>Export format<select aria-label="Table export format" value={format}
                                                                    disabled={busy}
                                                                    onChange={event => setFormat(event.target.value as typeof format)}>
            <option value="xlsx">Excel · XLSX</option>
            <option value="csv">CSV</option>
            <option value="tsv">TSV</option>
        </select></label>
            <button disabled={busy || !!reason}
                    onClick={() => void exported()}>{busy ? 'Preparing export…' : 'Export applied table'}</button>
        </div>
        {(reason || error) && <p role={error ? 'alert' : undefined}>{error ?? reason}</p>}</div>;
}

export function StudioTableImport({context, target, close}: {
    context: StudioContext;
    target: TableTarget;
    close: () => void
}) {
    const initialRows = useMemo(() => exportTable(context.graph, target), [context.graph, target]);
    const [format, setFormat] = useState<'csv' | 'tsv'>('csv'), [text, setText] = useState(() => tableText(initialRows, 'csv'));
    const [book, setBook] = useState<InputWorkbook>(() => parseTableText(tableText(initialRows, 'csv'), 'csv')), [sheetIndex, setSheetIndex] = useState(0), [headerRow, setHeaderRow] = useState('1');
    const [mapping, setMapping] = useState<ColumnMapping>(() => autoColumns(book.sheets[0], target)), [symbols, setSymbols] = useState<'id' | 'name'>('id'), [unit, setUnit] = useState<ImportOptions['unit']>('native');
    const [addedWeight, setAddedWeight] = useState('1'), [discardStops, setDiscardStops] = useState(false), [capPolicy, setCapPolicy] = useState<'keep' | 'recompute'>(target.kind === 'paytable' && target.binding.bound ? 'recompute' : 'keep');
    const [preview, setPreview] = useState<ImportPreview | null>(null), [error, setError] = useState<string | null>(null), [reading, setReading] = useState(false), [fileName, setFileName] = useState<string | null>(null), [previewPage, setPreviewPage] = useState(0);
    const [source] = useState(() => canonicalJson(context.graph)),
        changedSource = source !== canonicalJson(context.graph), dialog = useRef<HTMLDialogElement>(null),
        request = useRef(0), [readFormat, setReadFormat] = useState<'paste' | 'file'>('paste');
    useEffect(() => {
        const lifetime = request;
        dialog.current?.showModal();
        return () => {
            lifetime.current++;
        };
    }, []);
    const sheet = book.sheets[sheetIndex] ?? book.sheets[0], row = Number(headerRow) - 1,
        headers = sheet.rows[row] ?? [], fields = targetFields(target);
    const invalidate = () => {
        setPreview(null);
        setPreviewPage(0);
    };
    const loadBook = (next: InputWorkbook) => {
        setBook(next);
        setSheetIndex(0);
        setHeaderRow('1');
        setMapping(autoColumns(next.sheets[0], target));
        setError(null);
        invalidate();
    };
    const file = async (input: File) => {
        const current = ++request.current;
        setReading(true);
        invalidate();
        setError(null);
        setFileName(input.name);
        try {
            if (input.size > 10 * 1024 * 1024) throw new Error('Table files are limited to 10 MiB; text imports to 1 MiB.');
            const extension = input.name.split('.').at(-1)?.toLowerCase();
            if (!['xlsx', 'csv', 'tsv'].includes(extension ?? '')) throw new Error('Choose XLSX, CSV or TSV.');
            const result = extension === 'xlsx' ? await readSpreadsheet(new Uint8Array(await input.arrayBuffer())) : parseTableText(await input.text(), extension as 'csv' | 'tsv');
            if (current === request.current) {
                loadBook(result);
                setReadFormat('file');
            }
        } catch (failure) {
            if (current === request.current) setError(failure instanceof Error ? failure.message : 'The table could not be read.');
        } finally {
            if (current === request.current) setReading(false);
        }
    };
    const paste = (value: string, kind = format) => {
        request.current++;
        setReading(false);
        setText(value);
        setReadFormat('paste');
        setFileName(null);
        try {
            loadBook(parseTableText(value, kind));
        } catch (failure) {
            setPreview(null);
            setError(failure instanceof Error ? failure.message : 'Malformed table text.');
        }
    };
    const inspect = () => {
        if (error || changedSource) return;
        const result = previewTable(context.graph, target, sheet, {
            headerRow: row,
            mapping,
            symbols,
            unit,
            addedWeight,
            discardStops,
            capPolicy
        }, context.patches);
        setPreview(result);
    };
    return <dialog ref={dialog} className="studio-import-dialog studio-tabular-dialog" onCancel={close}>
        <header>
            <div><h2>Import into {target.label}</h2><p>Map literal cells, inspect exact changes, then stage one complete
                edit.</p></div>
            <button aria-label="Close import" onClick={close}>×</button>
        </header>
        <div className="studio-import-intake"><label>Choose table file<input type="file" aria-label="Choose table file"
                                                                             accept=".xlsx,.csv,.tsv" disabled={reading}
                                                                             onChange={event => {
                                                                                 const chosen = event.target.files?.[0];
                                                                                 if (chosen) void file(chosen);
                                                                                 event.target.value = '';
                                                                             }}/></label><label>Paste format<select
            aria-label="Paste table format" value={format} onChange={event => {
            const kind = event.target.value as typeof format;
            setFormat(kind);
            paste(text, kind);
        }}>
            <option value="csv">CSV · comma separated</option>
            <option value="tsv">TSV · tab separated</option>
        </select></label></div>
        <label>Paste table<textarea aria-label="CSV import input"
                                    value={readFormat === 'file' ? tableText(sheet.rows.map(row => row.map(cell => cell.text)), format) : text}
                                    disabled={reading || readFormat === 'file'}
                                    onChange={event => paste(event.target.value)} spellCheck={false}/></label>
        {readFormat === 'file' &&
            <button onClick={() => paste(tableText(sheet.rows.map(row => row.map(cell => cell.text)), format))}>Edit as
                pasted literals</button>}{fileName &&
        <p className="studio-import-file">{reading ? 'Reading ' : 'Loaded '}{fileName} · data stays in this browser</p>}
        <div className="studio-import-mapping"><label>Worksheet<select aria-label="Import worksheet" value={sheetIndex}
                                                                       disabled={reading} onChange={event => {
            const index = Number(event.target.value);
            setSheetIndex(index);
            setMapping(autoColumns(book.sheets[index], target, row));
            invalidate();
        }}>{book.sheets.map((sheet, index) => <option key={index} value={index}>{sheet.name}</option>)}</select></label><label>Header
            row<input aria-label="Import header row" inputMode="numeric" value={headerRow} onChange={event => {
                setHeaderRow(event.target.value);
                setMapping(autoColumns(sheet, target, Number(event.target.value) - 1));
                invalidate();
            }}/></label>
            {target.kind !== 'weights' && target.kind !== 'lines' &&
                <label>Symbol matching<select aria-label="Import symbol matching" value={symbols} onChange={event => {
                    setSymbols(event.target.value as typeof symbols);
                    invalidate();
                }}>
                    <option value="id">Exact symbol ID</option>
                    <option value="name">Exact displayed name</option>
                </select></label>}
            {target.kind === 'paytable' &&
                <label>Input payout unit<select aria-label="Import payout unit" value={unit} onChange={event => {
                    setUnit(event.target.value as typeof unit);
                    invalidate();
                }}>
                    <option value="native">{nativeUnit(target.binding)}</option>
                    <option value="credits">Credits</option>
                    <option value="round">Round-price multiples · {target.binding.roundCost} credits</option>
                    {Number.isSafeInteger(Number((context.graph.initialState as Record<string, unknown>)?.totalBet)) &&
                        <option value="bet">Total-bet multiples</option>}</select></label>}
        </div>
        <div className="studio-import-columns">{fields.map(field => <label
            key={field.field}>{field.label}{field.required ? ' · required' : ''}<select
            aria-label={`Map ${field.field} column`} value={mapping[field.field] ?? ''} onChange={event => {
            const next = {...mapping};
            if (event.target.value === '') delete next[field.field]; else next[field.field] = Number(event.target.value);
            setMapping(next);
            invalidate();
        }}>
            <option value="">{field.required ? 'Choose a column' : 'Do not import'}</option>
            {headers.map((cell, index) => <option key={index}
                                                  value={index}>{index + 1}: {cell.text || '(blank header)'}</option>)}
        </select></label>)}</div>
        {target.kind === 'reels' &&
            <div className="studio-import-policy"><label>Exact weight for newly added stops<input
                aria-label="Imported new-stop weight" value={addedWeight} inputMode="numeric" onChange={event => {
                setAddedWeight(event.target.value);
                invalidate();
            }}/></label><label className="studio-check"><input type="checkbox" checked={discardStops}
                                                               onChange={event => {
                                                                   setDiscardStops(event.target.checked);
                                                                   invalidate();
                                                               }}/>Discard weights of explicitly removed stops</label>
                <p>{mapping.stopId === undefined ? 'Retained stops match original indices. Reordering symbols here keeps each position’s original weight.' : 'Retained stops match their explicit IDs; imported indices define the new physical order.'}</p>
            </div>}
        {target.kind === 'paytable' && target.binding.cap &&
            <label>Payout cap policy<select aria-label="Import cap policy" value={capPolicy} onChange={event => {
                setCapPolicy(event.target.value as typeof capPolicy);
                invalidate();
            }}>{target.binding.bound && <option value="recompute">Recompute the source upper bound</option>}
                <option value="keep">Keep the current cap as a game rule</option>
            </select></label>}
        <p className="studio-import-policy-note">Import literal values. Formula cells and macros are rejected; external
            links are not opened. Original numbers and text are checked before
            conversion. {readFormat === 'file' ? `${book.format.toUpperCase()} · ` : ''}{sheet.rows.length} rows.
            Unmapped columns are ignored.</p>

        {preview && <div className="studio-import-preview">
            <b>{preview.errors.length ? `${preview.errors.length} import error${preview.errors.length === 1 ? '' : 's'}` : `${preview.changes.length} exact input rows reviewed`}</b>{preview.errors.length > 0 &&
            <div className="studio-import-row-errors" role="alert">
                <table>
                    <thead>
                    <tr>
                        <th>Row</th>
                        <th>Field</th>
                        <th>Correction</th>
                    </tr>
                    </thead>
                    <tbody>{preview.errors.map((error, index) => <tr key={index}>
                        <th>{error.row || '—'}</th>
                        <td>{error.field}</td>
                        <td>{error.message}</td>
                    </tr>)}</tbody>
                </table>
            </div>}
            <div className="studio-import-table-scroll">
                <table className="studio-matrix">
                    <thead>
                    <tr>
                        <th>Row / input</th>
                        <th>Current staged / applied</th>
                        <th>Imported value</th>
                    </tr>
                    </thead>
                    <tbody>{preview.changes.slice(previewPage * 100, (previewPage + 1) * 100).map((change, index) => <tr
                        key={index}>
                        <th>{change.row}: {change.item}</th>
                        <td>{change.before}</td>
                        <td>{change.after}</td>
                    </tr>)}</tbody>
                </table>
            </div>
            {preview.changes.length > 100 && <div className="studio-actions">
                <button disabled={previewPage === 0} onClick={() => setPreviewPage(page => page - 1)}>Previous preview
                    rows
                </button>
                <span>{previewPage * 100 + 1}–{Math.min(preview.changes.length, (previewPage + 1) * 100)} of {preview.changes.length}</span>
                <button disabled={(previewPage + 1) * 100 >= preview.changes.length}
                        onClick={() => setPreviewPage(page => page + 1)}>Next preview rows
                </button>
            </div>}<p>Only reviewed targets change. Matching staged coordinates are replaced; other pending edits stay
            staged; the complete candidate graph is validated when you apply.</p></div>}
        {(error || changedSource) && <p className="studio-error"
                                        role="alert">{changedSource ? 'The graph changed while this import was open. Close it and reopen against the current source.' : error}</p>}
        <footer className="studio-import-actions">
            <button disabled={reading || !!error || changedSource} onClick={inspect}>Preview exact values</button>
            <button className="studio-primary"
                    disabled={!preview?.patches.length || !!preview.errors.length || changedSource || context.disabled || reading}
                    onClick={() => {
                        context.stage(preview!.patches);
                        close();
                    }}>Stage import
            </button>
        </footer>
    </dialog>;
}
