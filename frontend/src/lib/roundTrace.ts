import type { GraphRound } from './graphApi';
export interface TraceHit { symbol: string; size: number; payout: number; positions: number[] }
export interface TraceStep { spin: number; before: string[]; after: string[]; multiplier: number; win: number; hits: TraceHit[] }
function numeric(value: unknown): number {
  if (typeof value === 'object' && value !== null && 'numerator' in value && 'denominator' in value) {
    const result = Number(value.numerator) / Number(value.denominator);
    if (Number.isFinite(result)) return result;
  }
  if ((typeof value === 'number' || typeof value === 'string') && value !== '' && Number.isFinite(Number(value))) return Number(value);
  throw new Error('Invalid numeric trace field.');
}
function array(value: unknown): unknown[] { if (!Array.isArray(value)) throw new Error('Missing trace array.'); return value; }
export function boardCells(value: unknown, size: number): string[] | null {
  if (!Array.isArray(value) || value.length !== size || !value.every(cell => typeof cell === 'string' || typeof cell === 'number')) return null;
  return value.map(String);
}
/** Read authored row-major histories. No reconstructed events or execution rules. */
export function cascadeTrace(round: GraphRound, size: number): TraceStep[] | null {
  try {
    const state = round.state, spins = array(state.stepSpins);
    const fields = ['stepBoardsBefore', 'stepBoardsAfter', 'stepMultipliers', 'stepWins', 'stepHitSymbols', 'stepHitSizes', 'stepHitTotals', 'stepHitPositions'];
    if (fields.some(key => array(state[key]).length !== spins.length)) return null;
    return spins.map((spin, index) => {
      const at = (key: string) => array(state[key])[index];
      const before = boardCells(at('stepBoardsBefore'), size), after = boardCells(at('stepBoardsAfter'), size);
      if (!before || !after) throw new Error('Invalid board shape.');
      const symbols = array(at('stepHitSymbols')), sizes = array(at('stepHitSizes')), payouts = array(at('stepHitTotals')), positions = array(at('stepHitPositions'));
      if ([sizes, payouts, positions].some(values => values.length !== symbols.length)) throw new Error('Incomplete hit trace.');
      const hits = symbols.map((symbol, hit) => {
        if (typeof symbol !== 'string' && typeof symbol !== 'number') throw new Error('Invalid symbol.');
        const cells = array(positions[hit]).map(numeric);
        if (cells.some(cell => !Number.isInteger(cell) || cell < 0 || cell >= size)) throw new Error('Invalid cell index.');
        return { symbol: String(symbol), size: numeric(sizes[hit]), payout: numeric(payouts[hit]), positions: cells };
      });
      return { spin: numeric(spin), before, after, multiplier: numeric(at('stepMultipliers')), win: numeric(at('stepWins')), hits };
    });
  } catch { return null; }
}
