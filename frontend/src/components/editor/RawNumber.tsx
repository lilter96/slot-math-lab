import { useEffect, useRef, useState } from 'react';
import { Ic } from '../Icons';

export default function RawNumber({ value, label, display = value, detail }: { value: string; label: string; display?: string; detail?: string }) {
  const [status, setStatus] = useState<{ value: string; failed: boolean } | null>(null);
  const alive = useRef(true), latest = useRef(value), task = useRef(0);
  useEffect(() => { latest.current = value; task.current++; }, [value]);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  return <span className="raw-number">
    <span>{display}{detail && <small>{detail}</small>}</span>
    <button type="button" className="raw-copy" aria-label={`Copy raw ${label}`} title={`Copy ${value}`} onClick={async () => {
      const token = ++task.current;
      let failed = false;
      try { if (!navigator.clipboard) throw new Error('Clipboard unavailable'); await navigator.clipboard.writeText(value); }
      catch { failed = true; }
      if (alive.current && latest.current === value && task.current === token) setStatus({ value, failed });
    }}><Ic.copy /></button>
    {status?.value === value && <span className="raw-copy-status" role="status">{status.failed ? <>Select and copy:<input className="inp" aria-label={`Raw ${label}`} readOnly value={value} onFocus={event => event.target.select()} /></> : 'Copied'}</span>}
  </span>;
}
