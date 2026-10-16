/** Keep destructive confirmation at the UI boundary, outside model commands. */
export function confirmGraphDelete(nodes: number, edges: number): boolean {
  const items = [nodes ? `${nodes} node${nodes === 1 ? '' : 's'}` : '', edges ? `${edges} connection${edges === 1 ? '' : 's'}` : ''].filter(Boolean).join(' and ');
  return !!items && window.confirm(`Delete ${items}? Connected edges will also be removed. Export a backup first if you need to keep this source.`);
}
