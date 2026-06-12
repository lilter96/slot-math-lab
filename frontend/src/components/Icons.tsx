import type { SVGProps } from 'react';

type SvgProps = SVGProps<SVGSVGElement>;

export const Ic = {
  draw: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 7h16M4 12h16M4 17h16" strokeLinecap="round" />
      <circle cx="8" cy="7" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="15" cy="12" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="10" cy="17" r="1.6" fill="currentColor" stroke="none" />
    </svg>
  ),
  evaluator: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
      <path d="M3.5 9.5h17M9 9.5v10M15 9.5v10" />
    </svg>
  ),
  predicate: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3l8 9-8 9-8-9 8-9z" />
      <path d="M9.5 12l1.8 1.8 3.2-3.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  expr: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path
        d="M7 5c-1.5 0-2 1-2 3v2c0 1.3-.6 2-1.5 2 .9 0 1.5.7 1.5 2v2c0 2 .5 3 2 3M17 5c1.5 0 2 1 2 3v2c0 1.3.6 2 1.5 2-.9 0-1.5.7-1.5 2v2c0 2-.5 3-2 3"
        strokeLinecap="round"
      />
    </svg>
  ),
  loop: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 9a7 7 0 0111.5-3.5L19 8M20 15a7 7 0 01-11.5 3.5L5 16" strokeLinecap="round" />
      <path d="M19 4v4h-4M5 20v-4h4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  sink: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M5 19V9M10 19V5M15 19v-7M20 19v-4" strokeLinecap="round" />
      <path d="M3 19h18" strokeLinecap="round" />
    </svg>
  ),
  build: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <circle cx="6" cy="6" r="2.5" />
      <circle cx="18" cy="6" r="2.5" />
      <circle cx="12" cy="18" r="2.5" />
      <path d="M8.5 6H18M6 8.5v4a3 3 0 003 3h0M18 8.5v4a3 3 0 01-3 3h-2" strokeLinecap="round" />
    </svg>
  ),
  sim: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path
        d="M3 16c2-6 4-9 5-9s1.5 4 2.5 4 2-9 3.5-9 2.5 7 3.5 9 2 1 3.5 1"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  ),
  results: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="4" y="3" width="16" height="18" rx="2" />
      <path d="M8 8h8M8 12h8M8 16h5" strokeLinecap="round" />
    </svg>
  ),
  export: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3v12M12 3L8 7M12 3l4 4" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M5 14v4a2 2 0 002 2h10a2 2 0 002-2v-4" strokeLinecap="round" />
    </svg>
  ),
  ai: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M12 3l1.8 4.2L18 9l-4.2 1.8L12 15l-1.8-4.2L6 9l4.2-1.8L12 3z" strokeLinejoin="round" />
      <path d="M18 14l.9 2.1 2.1.9-2.1.9-.9 2.1-.9-2.1-2.1-.9 2.1-.9.9-2.1z" strokeLinejoin="round" />
    </svg>
  ),
  plus: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M12 5v14M5 12h14" strokeLinecap="round" />
    </svg>
  ),
  minus: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M5 12h14" strokeLinecap="round" />
    </svg>
  ),
  fit: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <path d="M4 9V5a1 1 0 011-1h4M20 9V5a1 1 0 00-1-1h-4M4 15v4a1 1 0 001 1h4M20 15v4a1 1 0 01-1 1h-4" strokeLinecap="round" />
    </svg>
  ),
  play: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="currentColor" stroke="none" {...p}>
      <path d="M7 5l12 7-12 7V5z" />
    </svg>
  ),
  stop: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="currentColor" stroke="none" {...p}>
      <rect x="6" y="6" width="12" height="12" rx="2" />
    </svg>
  ),
  copy: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 012-2h8" strokeLinecap="round" />
    </svg>
  ),
  check: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" {...p}>
      <path d="M5 12.5l4.5 4.5L19 7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  ),
  x: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" {...p}>
      <path d="M6 6l12 12M18 6L6 18" strokeLinecap="round" />
    </svg>
  ),
  target: (p: SvgProps) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" {...p}>
      <circle cx="12" cy="12" r="8" />
      <circle cx="12" cy="12" r="3.5" />
    </svg>
  ),
};

export const NODE_ACCENT: Record<string, string> = {
  draw: 'var(--n-draw)',
  evaluator: 'var(--n-eval)',
  library: 'var(--n-eval)',
  predicate: 'var(--n-eval)',
  expr: 'var(--n-expr)',
  loop: 'var(--n-loop)',
  sink: 'var(--n-sink)',
};
