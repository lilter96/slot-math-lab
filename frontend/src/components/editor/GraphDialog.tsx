import { useEffect, useRef, type ReactNode } from 'react';
import './GraphDialog.css';
export default function GraphDialog({ title, onClose, children, wide = false }: {
  title: string; onClose: () => void; children: ReactNode; wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { const dialog = ref.current; dialog?.showModal(); return () => dialog?.close(); }, []);
  return <dialog ref={ref} className={'dh-dialog' + (wide ? ' dh-dialog-wide' : '')}
    aria-label={title} onCancel={e => { e.preventDefault(); onClose(); }}>
    <header><h2>{title}</h2><button type="button" aria-label="Close dialog" onClick={onClose}>×</button></header>
    <div className="dh-dialog-content">{children}</div>
  </dialog>;
}
