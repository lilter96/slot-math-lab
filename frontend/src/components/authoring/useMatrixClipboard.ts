import { useState, type ClipboardEvent, type KeyboardEvent, type MouseEvent } from 'react';
import { parseTableText, tableText } from '../../lib/authoring/spreadsheetIO';
interface Position { row: number; column: number }
const coordinate = (target: EventTarget | null): Position | null => {
  if (!(target instanceof HTMLInputElement)) return null;
  const row = Number(target.dataset.matrixRow), column = Number(target.dataset.matrixColumn);
  return Number.isSafeInteger(row) && Number.isSafeInteger(column) ? { row, column } : null;
};
/** One rectangular gesture stages one batch; it never mutates the executable graph. */
export function useMatrixClipboard(values: (string | null)[][], stage: (cells: { row: number; column: number; value: string }[]) => void, disabled: boolean) {
  const [anchor, setAnchor] = useState<Position | null>(null), [end, setEnd] = useState<Position | null>(null), [error, setError] = useState<string | null>(null);
  const bounds = anchor && end ? { top: Math.min(anchor.row, end.row), bottom: Math.max(anchor.row, end.row), left: Math.min(anchor.column, end.column), right: Math.max(anchor.column, end.column) } : null;
  const selected = (row: number, column: number) => !!bounds && row >= bounds.top && row <= bounds.bottom && column >= bounds.left && column <= bounds.right;
  const click = (event: MouseEvent<HTMLDivElement>) => { const position = coordinate(event.target); if (!position) return; if (!event.shiftKey || !anchor) setAnchor(position); setEnd(position); setError(null); };
  const key = (event: KeyboardEvent<HTMLDivElement>) => {
    const position = coordinate(event.target); if (!position || disabled || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || event.ctrlKey || event.metaKey || event.altKey) return;
    const next = { row: position.row + (event.key === 'ArrowUp' ? -1 : event.key === 'ArrowDown' ? 1 : 0), column: position.column + (event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0) };
    const input = event.currentTarget.querySelector<HTMLInputElement>(`input[data-matrix-row="${next.row}"][data-matrix-column="${next.column}"]`);
    if (!input || input.disabled) return; event.preventDefault(); input.focus(); input.select(); if (!event.shiftKey || !anchor) setAnchor(event.shiftKey ? position : next); setEnd(next); setError(null);
  };
  const paste = (event: ClipboardEvent<HTMLDivElement>) => {
    const start = coordinate(event.target), text = event.clipboardData.getData('text/plain'); if (!start || disabled || (!text.includes('\t') && !text.includes('\n') && !text.includes('\r'))) return;
    event.preventDefault();
    try {
      const rows = parseTableText(text, 'tsv').sheets[0].rows, width = rows[0]?.length ?? 0;
      if (!width || rows.some(row => row.length !== width)) throw new Error('Paste a complete rectangular TSV selection.');
      const cells: { row: number; column: number; value: string }[] = [];
      rows.forEach((row, i) => row.forEach((cell, j) => { const r = start.row + i, c = start.column + j; if (values[r]?.[c] === undefined || values[r][c] === null) throw new Error('The pasted rectangle includes a missing or unsupported cell. No input has changed.'); cells.push({ row: r, column: c, value: cell.text }); }));
      stage(cells); setAnchor(start); setEnd({ row: start.row + rows.length - 1, column: start.column + width - 1 }); setError(null);
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The clipboard selection could not be staged.'); }
  };
  const copy = (event: ClipboardEvent<HTMLDivElement>) => {
    if (!bounds || !(event.target instanceof HTMLInputElement)) return;
    if (bounds.top === bounds.bottom && bounds.left === bounds.right && event.target.selectionStart !== event.target.selectionEnd) return;
    const rows = values.slice(bounds.top, bounds.bottom + 1).map(row => row.slice(bounds.left, bounds.right + 1));
    if (rows.some(row => row.some(value => value === null))) { setError('This selection contains unsupported cells. Select only editable values.'); return; }
    event.clipboardData.setData('text/plain', tableText(rows as string[][], 'tsv')); event.preventDefault();
  };
  return { selected, error, handlers: { onClick: click, onKeyDown: key, onPaste: paste, onCopy: copy }, description: bounds ? `${bounds.bottom - bounds.top + 1} × ${bounds.right - bounds.left + 1} cells selected. Shift + arrows or Shift + click extends selection; paste TSV to stage a rectangle.` : 'Use arrows to move; Shift + arrows selects a rectangle. Copy or paste TSV values.' };
}
