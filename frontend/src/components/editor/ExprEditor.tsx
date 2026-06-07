import { useRef, useEffect, useCallback } from 'react';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap, lineNumbers, highlightActiveLine, placeholder as cmPlaceholder } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { syntaxHighlighting, defaultHighlightStyle } from '@codemirror/language';
import { autocompletion, type Completion } from '@codemirror/autocomplete';
import { linter, type Diagnostic } from '@codemirror/lint';
import { analyze, getKnownFunctions, type ExprContext } from './expr-validator';

interface ExprEditorProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  ctx?: ExprContext;
  className?: string;
  /** For display only — the outer component determines visibility */
  style?: React.CSSProperties;
}

/** Build autocomplete sources from context + known functions. */
function buildCompletions(ctx: ExprContext): Completion[] {
  const result: Completion[] = [];

  // Board fields
  result.push({
    label: 'board',
    type: 'keyword',
    detail: 'Board access',
    info: 'Access board cells and properties',
  });

  // State fields
  if (ctx.state) {
    for (const [key, val] of Object.entries(ctx.state)) {
      result.push({
        label: `state.${key}`,
        type: 'property',
        detail: typeof val === 'string' ? val : 'object',
      });
    }
  }

  // Symbol references
  if (ctx.symbols) {
    for (const sym of ctx.symbols) {
      result.push({
        label: sym.id,
        type: 'variable',
        detail: `${sym.kind} symbol`,
        info: `Symbol: ${sym.id} (${sym.kind})`,
      });
    }
  }

  // Known functions
  const fns = getKnownFunctions();
  for (const fn of fns) {
    result.push({
      label: fn.name,
      type: 'function',
      detail: `→ ${fn.returnType}`,
      info: fn.desc,
      apply: fn.name + '()',
    });
  }

  // Keywords
  for (const kw of ['if', 'and', 'or', 'not', 'true', 'false']) {
    result.push({ label: kw, type: 'keyword' });
  }

  return result;
}

export default function ExprEditor({ value, onChange, placeholder, ctx = {}, style }: ExprEditorProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const onChangeRef = useRef(onChange);
  const ctxRef = useRef(ctx);

  useEffect(() => { onChangeRef.current = onChange; });
  useEffect(() => { ctxRef.current = ctx; });

  // Create lint source from our validator
  const lintSource = useCallback(
    (view: EditorView) => {
      const text = view.state.doc.toString();
      const currentCtx = ctxRef.current;
      const analysis = analyze(text, currentCtx);
      const diagnostics: Diagnostic[] = [];

      if (text.trim()) {
        for (const err of analysis.errors) {
          diagnostics.push({
            from: err.pos ?? 0,
            to: (err.pos ?? 0) + 1,
            severity: 'error',
            message: err.msg,
          });
        }
      }
      return diagnostics;
    },
    [],
  );

  useEffect(() => {
    if (!containerRef.current) return;

    const updateListener = EditorView.updateListener.of((update) => {
      if (update.docChanged) {
        const newValue = update.state.doc.toString();
        onChangeRef.current(newValue);
      }
    });

    const autocompleteExtension = autocompletion({
      override: [
        (context) => {
          const completions = buildCompletions(ctxRef.current);
          return { from: context.pos, options: completions };
        },
      ],
    });

    const lintExtension = linter(lintSource);

    const state = EditorState.create({
      doc: value,
      extensions: [
        lineNumbers(),
        highlightActiveLine(),
        history(),
        updateListener,
        autocompleteExtension,
        lintExtension,
        keymap.of([...defaultKeymap, ...historyKeymap]),
        syntaxHighlighting(defaultHighlightStyle),
        EditorView.theme({
          '&': {
            background: 'var(--bg)',
            color: 'var(--text)',
            fontFamily: 'var(--mono)',
            fontSize: '12.5px',
            borderRadius: '7px',
            border: '1px solid var(--line)',
          },
          '&.cm-focused': {
            outline: 'none',
            borderColor: 'var(--sampled)',
            boxShadow: '0 0 0 3px var(--sampled-dim)',
          },
          '.cm-content': {
            caretColor: 'var(--exact)',
            padding: '6px 8px',
            minHeight: '32px',
          },
          '.cm-lineNumbers .cm-gutterElement': {
            color: 'var(--faint)',
            fontSize: '10px',
            padding: '0 6px',
          },
          '.cm-gutters': {
            background: 'var(--bg)',
            borderRight: '1px solid var(--line)',
            color: 'var(--faint)',
          },
          '.cm-activeLine': { background: 'var(--bg-2) !important' },
          '.cm-tooltip': {
            background: 'var(--bg-2)',
            border: '1px solid var(--line-2)',
            borderRadius: '8px',
            boxShadow: 'var(--shadow-pop)',
            color: 'var(--text)',
            fontFamily: 'var(--mono)',
            fontSize: '12px',
          },
          '.cm-tooltip-autocomplete ul li': {
            padding: '4px 10px',
          },
          '.cm-tooltip-autocomplete ul li[aria-selected]': {
            background: 'var(--bg-3)',
            color: 'var(--text)',
          },
          '.cm-diagnostic': {
            borderBottom: '2px dotted var(--danger)',
          },
          '.cm-lintRange-error': {
            backgroundImage: 'none',
            borderBottom: '2px wavy var(--danger)',
          },
          '.cm-diagnosticText': {
            background: 'var(--danger-dim)',
            color: 'var(--danger)',
            border: '1px solid var(--danger)',
            borderRadius: '4px',
            padding: '4px 8px',
            fontFamily: 'var(--mono)',
            fontSize: '11px',
          },
        }),
        placeholder ? cmPlaceholder(placeholder) : [],
      ],
    });

    const view = new EditorView({
      state,
      parent: containerRef.current,
    });

    viewRef.current = view;

    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Sync external value changes
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    const currentDoc = view.state.doc.toString();
    if (value !== currentDoc) {
      view.dispatch({
        changes: { from: 0, to: currentDoc.length, insert: value },
      });
    }
  }, [value]);

  return <div ref={containerRef} style={style} />;
}
