import { useId, useState, type CSSProperties } from 'react';
import { parseModelNumber } from '../../lib/modelJson';

export default function ModelNumberInput({ value, onChange, label, id, integer = false, min, style }: {
  value: number; onChange(value: number): void; label?: string; id?: string;
  integer?: boolean; min?: number; style?: CSSProperties;
}) {
  const errorId = useId();
  const [text, setText] = useState(String(value));
  const [source, setSource] = useState(value);
  const [error, setError] = useState('');
  if (!Object.is(source, value)) {
    setSource(value);
    if (error || !Object.is(Number(text), value)) { setText(String(value)); setError(''); }
  }
  return <span style={{ display: 'block' }}>
    <input id={id} className="inp" aria-label={label} aria-invalid={!!error} aria-describedby={error ? errorId : undefined}
      type="text" inputMode="decimal" value={text} style={style} onChange={event => {
        const next = event.target.value; setText(next);
        try {
          const number = parseModelNumber(next);
          if (integer && !Number.isInteger(number)) throw new Error('Enter a whole number. This edit has not been applied.');
          if (min !== undefined && number < min) throw new Error(`Enter a number at least ${min}. This edit has not been applied.`);
          onChange(number); setError('');
        } catch (reason) { setError(reason instanceof Error ? reason.message : 'This number has not been applied.'); }
      }} />
    {error && <span id={errorId} role="alert" className="hint" style={{ display: 'block', color: 'var(--danger)' }}>{error}</span>}
  </span>;
}
