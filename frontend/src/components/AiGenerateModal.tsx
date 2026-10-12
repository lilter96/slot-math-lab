import { useState, useCallback, useRef, useEffect } from 'react';
import { Ic } from './Icons';
import { useAppStore } from '../store';
import { parseModelJson } from '../lib/modelJson';
import { generatedModel } from '../lib/generatedModel';
import { projectContentScope, switchProject } from '../lib/projectFiles';
import { graphRequest } from '../lib/graphApi';

interface AiGenerateModalProps {
  onClose: () => void;
}

export default function AiGenerateModal({ onClose }: AiGenerateModalProps) {
  const [prompt, setPrompt] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const pending = useRef<{ controller: AbortController; scope: string } | null>(null);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    const unsubscribe = useAppStore.subscribe(() => {
      const task = pending.current;
      if (task && task.scope !== projectContentScope()) task.controller.abort();
    });
    return () => { mounted.current = false; pending.current?.controller.abort(); pending.current = null; unsubscribe(); };
  }, []);

  // Focus textarea on mount
  useEffect(() => {
    const trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const dialog = dialogRef.current;
    dialog?.showModal(); textareaRef.current?.focus();
    return () => { dialog?.close(); requestAnimationFrame(() => { if (trigger?.isConnected) trigger.focus(); }); };
  }, []);

  // Close on Escape key
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [onClose]);

  const handleGenerate = useCallback(async () => {
    if (!prompt.trim() || pending.current) return;
    const task = { controller: new AbortController(), scope: projectContentScope() };
    pending.current = task;
    const current = () => mounted.current && pending.current === task && !task.controller.signal.aborted && projectContentScope() === task.scope;

    setLoading(true);
    setError(null);

    try {
      const res = await fetch('/api/ai/generate-graph', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt: prompt.trim() }),
        signal: task.controller.signal,
      });

      if (res.status === 503) {
        if (current()) setError('AI service is not configured on this server.');
        return;
      }

      const data = res.ok ? parseModelJson(await res.text()) : await res.json().catch(() => ({}));
      if (!current()) return;

      if (!res.ok) {
        // 422 with compiler errors or 400
        const errData = data as { error?: string; title?: string; compilerErrors?: Array<{ message: string }> };
        const msgs = (Array.isArray(errData.compilerErrors) ? errData.compilerErrors.map(error => error.message).filter(Boolean).join('\n') : '') || errData.error || errData.title || `Generation failed (HTTP ${res.status}).`;
        setError(msgs);
        return;
      }

      const config = generatedModel(data);
      const checked = await graphRequest<{ isValid: boolean; errors?: { message: string }[] }>('validate', { config }, task.controller.signal);
      if (!current()) return;
      if (checked.isValid !== true) throw new Error(checked.errors?.map(error => error.message).join('; ') || 'Generated model failed compiler validation.');
      pending.current = null;
      switchProject(config);
      onClose();
    } catch (err) {
      if (mounted.current && pending.current === task) setError(task.controller.signal.aborted ? 'The source changed while generating. Generate again for the current workspace.' : err instanceof Error ? err.message : 'Request failed');
    } finally {
      if (mounted.current) setLoading(false);
      if (pending.current === task) pending.current = null;
    }
  }, [prompt, onClose]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      void handleGenerate();
    }
  }, [handleGenerate]);

  return (
    <dialog ref={dialogRef} onKeyDown={event => {
      if (event.key !== 'Tab') return;
      const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not([disabled]), textarea:not([disabled])'));
      const first = controls[0], last = controls.at(-1);
      if (event.shiftKey && document.activeElement === first && last) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last && first) { event.preventDefault(); first.focus(); }
    }} onCancel={event => { event.preventDefault(); onClose(); }}
      style={{ padding: 0, border: 0, background: 'transparent', color: 'inherit', width: 520, maxWidth: '90vw', maxHeight: '90vh', overflowY: 'auto', borderRadius: 'var(--radius-lg)' }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      role="dialog"
      aria-modal="true"
      aria-label="AI Generate Graph"
    >
      <div
        style={{
          background: 'var(--bg-1)',
          border: '1px solid var(--line)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-pop)',
          width: '520px',
          maxWidth: '90vw',
          display: 'flex',
          flexDirection: 'column',
          gap: 0,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          style={{
            padding: '14px 16px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <span
            style={{
              display: 'grid',
              placeItems: 'center',
              width: 28,
              height: 28,
              borderRadius: 8,
              background: 'var(--bg-3)',
              color: 'var(--exact)',
              flexShrink: 0,
            }}
          >
            <Ic.ai style={{ width: 16, height: 16 }} />
          </span>
          <div>
            <div style={{ fontWeight: 600, fontSize: 13 }}>AI Generate Graph</div>
            <div style={{ fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
              describe your slot game in plain English
            </div>
          </div>
          <button
            onClick={onClose}
            style={{
              marginLeft: 'auto',
              background: 'none',
              border: 'none',
              color: 'var(--muted)',
              cursor: 'pointer',
              padding: 4, minWidth: 44, minHeight: 44,
              borderRadius: 6,
              display: 'grid',
              placeItems: 'center',
            }}
            title="Close"
            aria-label="Close"
          >
            <Ic.x style={{ width: 16, height: 16 }} />
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: '16px' }}>
          <div className="field">
            <label htmlFor="ai-prompt">Game description</label>
            <textarea
              id="ai-prompt"
              ref={textareaRef}
              className="inp"
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="e.g. A 5-reel 3-row slot with 20 paylines, cherries, lemons, bells and a wild. Include a scatter that triggers 10 free spins."
              rows={5}
              style={{ resize: 'vertical', minHeight: 90 }}
              disabled={loading}
            />
            <div className="hint">
              Ctrl+Enter to generate a separate private project. Your current draft is retained; generated content is checked by the compiler.
            </div>
          </div>

          {error && (
            <div
              style={{
                background: 'var(--danger-dim)',
                border: '1px solid var(--danger)',
                borderRadius: 'var(--radius)',
                padding: '10px 12px',
                fontSize: 12,
                color: 'var(--danger)',
                fontFamily: 'var(--mono)',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                marginTop: 4,
              }}
            >
              <span role="alert">{error}</span>
            </div>
          )}
        </div>

        {/* Footer */}
        <div
          style={{
            padding: '12px 16px',
            borderTop: '1px solid var(--line)',
            display: 'flex',
            gap: 10,
            justifyContent: 'flex-end',
            alignItems: 'center',
          }}
        >
          {loading && (
            <span style={{ fontSize: 11, color: 'var(--faint)', fontFamily: 'var(--mono)' }}>
              generating…
            </span>
          )}
          <button className="btn" onClick={onClose} >
            Cancel
          </button>
          <button
            className="btn primary"
            onClick={handleGenerate}
            disabled={loading || !prompt.trim()}
          >
            <Ic.ai style={{ width: 14, height: 14 }} />
            {loading ? 'Generating…' : 'Generate'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
