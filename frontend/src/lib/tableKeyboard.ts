import type { KeyboardEvent } from 'react';

/** Keep typing shortcuts intact; Enter or Alt+vertical arrow moves within the editing column. */
export function tableKeyboard(event: KeyboardEvent<HTMLTableElement>) {
  if (event.ctrlKey || event.metaKey || (event.key !== 'Enter' && !(event.altKey && ['ArrowUp', 'ArrowDown'].includes(event.key)))) return;
  if (!(event.target instanceof HTMLInputElement) || event.target.readOnly || event.target.disabled) return;
  const fields = [...event.currentTarget.querySelectorAll<HTMLInputElement>('tbody input:not([readonly]):not(:disabled)')];
  const index = fields.indexOf(event.target), direction = event.key === 'ArrowUp' || (event.key === 'Enter' && event.shiftKey) ? -1 : 1;
  const next = fields[index + direction];
  event.preventDefault();
  if (index >= 0 && next) { next.focus(); next.select(); next.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
}
