import { z } from 'zod';

// ── Symbol ──────────────────────────────────────────────────────────
export const SymbolKind = z.enum([
  'Standard', 'Wild', 'Scatter', 'Bonus', 'Multiplier', 'Money', 'Jackpot',
]);
export type SymbolKind = z.infer<typeof SymbolKind>;

export const SymbolDef = z.object({
  id: z.string().min(1, 'Symbol ID is required'),
  name: z.string().min(1, 'Symbol name is required'),
  kind: SymbolKind,
  color: z.string().regex(/^#[0-9a-fA-F]{6}$/, 'Must be a hex color like #ff0000'),
  properties: z.record(z.string(), z.string()).optional(),
});
export type SymbolDef = z.infer<typeof SymbolDef>;

export const SymbolTable = z.array(SymbolDef);
export type SymbolTable = z.infer<typeof SymbolTable>;

// ── Paytable Entry ──────────────────────────────────────────────────
export const PaytableEntry = z.object({
  symbolId: z.string(),
  counts: z.array(z.number().int().min(1)),
  payouts: z.array(z.number().min(0)),
});
export type PaytableEntry = z.infer<typeof PaytableEntry>;

export const Paytable = z.object({
  id: z.string().min(1, 'Paytable ID is required'),
  entries: z.array(PaytableEntry).min(1, 'At least one paytable entry required'),
});
export type Paytable = z.infer<typeof Paytable>;

// ── Reel Strip ──────────────────────────────────────────────────────
export const ReelStrip = z.object({
  id: z.string().min(1, 'Strip ID is required'),
  name: z.string().min(1, 'Strip name is required'),
  symbols: z.array(z.string()).min(1, 'Strip must contain at least 1 symbol'),
});
export type ReelStrip = z.infer<typeof ReelStrip>;

export const ReelSet = z.object({
  id: z.string().min(1, 'Reel set ID is required'),
  name: z.string().min(1, 'Reel set name is required'),
  stripIds: z.array(z.string()).min(1, 'Must reference at least 1 strip'),
});
export type ReelSet = z.infer<typeof ReelSet>;

// ── Board Config ────────────────────────────────────────────────────
export const BoardConfig = z.object({
  rows: z.number().int().min(1).max(20).default(3),
  columns: z.number().int().min(1).max(20).default(5),
  allowMultiSymbol: z.boolean().default(false),
  allowEmpty: z.boolean().default(false),
  allowLocked: z.boolean().default(false),
  growable: z.boolean().default(false),
});
export type BoardConfig = z.infer<typeof BoardConfig>;
