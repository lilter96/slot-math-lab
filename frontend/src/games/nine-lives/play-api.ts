import { createNineLivesGraph } from './graph';
import { peekRootProject } from '../../lib/projectFiles';
import { config, SYMBOLS, type LivesResult, type Response } from './types';

interface GraphRound {
  configHash: string;
  seed: number;
  roundIndex: number;
  state: Record<string, unknown>;
}
interface Pending { seed: number; index: number; multiplier: number; buy: boolean; config?: Record<string, unknown> }
interface Saved {
  version: 1;
  nextIndex: number;
  response: Response;
  frames: LivesResult[];
  cursor: number;
  pending?: Pending;
}
const key = 'slot-math-lab:nine-lives:v1';
const initialGrid = () => Array.from({ length: 6 }, () => [1, 2, 3, 4, 5]);
export class GraphApiError extends Error {
  readonly status: number;
  constructor(message: string, status: number) { super(message); this.status = status; }
}
const numbers = (value: unknown): number[] => {
  if (!Array.isArray(value)) throw new Error('Missing array in graph trace.');
  return value.map(number);
};
const number = (value: unknown): number => {
  const result = typeof value === 'object' && value !== null && 'numerator' in value && 'denominator' in value
    ? Number(value.numerator) / Number(value.denominator) : Number(value);
  if (!Number.isFinite(result)) throw new Error('Invalid number in graph trace.');
  return result;
};
const grid = (value: unknown) => {
  const cells = numbers(value);
  if (cells.length !== 30) throw new Error('Graph returned an invalid board.');
  return Array.from({ length: 6 }, (_, col) => Array.from({ length: 5 }, (_, row) => cells[row * 6 + col]));
};
const position = (rowMajor: number) => (rowMajor % 6) * 5 + Math.floor(rowMajor / 6);

// Presentation adapter only: all awards, collections, cascades and termination
// come from the graph trace. Conversion to bet-level credits changes units.
export function framesFromTrace(raw: GraphRound, multiplier: number, buy: boolean): LivesResult[] {
  const state = raw.state;
  const arrays = (name: string): unknown[] => {
    if (!Array.isArray(state[name])) throw new Error('Missing trace field: ' + name);
    return state[name];
  };
  const value = (name: string, index: number) => arrays(name)[index];
  const boards = arrays('boards');
  const stepSpins = numbers(state.stepSpins);
  const triggered = state.bonusTriggered === true;
  const frames: LivesResult[] = boards.map((board, index) => {
    const steps = stepSpins.flatMap((spin, step) => spin === index ? [{
      before: grid(value('stepBoardsBefore', step)), after: grid(value('stepBoardsAfter', step)),
      vanished: numbers(value('stepVanished', step)).map(position), multiplier: number(value('stepMultipliers', step)), win: number(value('stepWins', step)) * multiplier,
      hits: numbers(value('stepHitSymbols', step)).map((symbolId, hit) => ({ symbolId, symbolName: SYMBOLS[symbolId],
        size: numbers(value('stepHitSizes', step))[hit], basePayout: numbers(value('stepHitPayouts', step))[hit] * multiplier,
        totalPayout: numbers(value('stepHitTotals', step))[hit] * multiplier,
        positions: numbers((value('stepHitPositions', step) as unknown[])[hit]).map(position) })),
    }] : []);
    const free = buy || index > 0;
    const roundWin = number(value('roundWinHistory', index)) * multiplier;
    return { type: free ? 'FREE' : 'BASE', grid: grid(board), finalGrid: grid(value('finalBoards', index)), steps,
      coins: numbers(value('coinPositionHistory', index)).map((p, coin) => ({ position: position(p), value: numbers(value('coinValueHistory', index))[coin] * multiplier })),
      win: number(value('winHistory', index)) * multiplier, clusterWin: number(value('clusterWinHistory', index)) * multiplier,
      collectionWin: number(value('collectionWinHistory', index)) * multiplier, scatters: free ? 0 : number(state.scatters),
      bonusTriggered: !free && triggered, bonusEnded: free && index === boards.length - 1,
      remaining: number(value('remainingHistory', index)), multiplier: number(value('multiplierHistory', index)), roundWin,
      capped: roundWin === config.maxWinX * config.baseCost * multiplier };
  });
  if (buy) {
    const dummy = initialGrid();
    for (const p of [0, 9, 20, 29]) dummy[Math.floor(p / 5)][p % 5] = 7;
    frames.unshift({ type: 'BUY', grid: dummy, finalGrid: dummy, steps: [], coins: [], win: 0, collectionWin: 0, clusterWin: 0,
      scatters: 4, bonusTriggered: true, bonusEnded: false, remaining: config.freeSpins, multiplier: 1, roundWin: 0, capped: false });
  }
  return frames;
}

export class GraphGameApi {
  private saved: Saved;
  private replaying = false;
  private controller: AbortController | undefined;
  constructor() {
    const stored = localStorage.getItem(key);
    this.saved = stored ? JSON.parse(stored) as Saved : {
      version: 1, nextIndex: 0, frames: [], cursor: 0,
      response: { balance: 100000, revision: 0, state: { phase: 'BASE', triggeringMultiplier: 1, remaining: 0, multiplier: 1, roundWin: 0, bonusWin: 0, lastGrid: initialGrid() } },
    };
    if (this.saved.version !== 1) throw new Error('Unsupported Nine Lives saved session.');
  }
  get hasPending() { return this.saved.pending !== undefined; }
  get response() { return this.saved.response; }
  private persist() { if (!this.replaying) localStorage.setItem(key, JSON.stringify(this.saved)); }
  async restore(): Promise<Response> { return this.response; }
  resetExpiredSession() {
    localStorage.removeItem(key);
    this.saved = new GraphGameApi().saved;
    this.replaying = false;
  }
  dispose() { this.controller?.abort(); }
  private pinnedConfig(buy: boolean) {
    const draft = peekRootProject();
    const graph = structuredClone(Array.isArray(draft?.nodes) && draft.nodes.length ? draft : createNineLivesGraph());
    graph.initialState = { ...(graph.initialState as Record<string, unknown>), entryBuy: buy, entryCost: buy ? config.buyCost : 1 };
    graph.id = buy ? 'nine-lives-buy' : 'nine-lives-base';
    return graph;
  }
  private async request(pending: Pending): Promise<GraphRound> {
    this.controller = new AbortController();
    const response = await fetch((import.meta.env.VITE_API_URL ?? '') + '/api/play/round', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: this.controller.signal,
      body: JSON.stringify({ config: pending.config ?? this.pinnedConfig(pending.buy), seed: pending.seed, roundIndex: pending.index, trace: true }),
    });
    if (!response.ok) throw new GraphApiError(`Unable to execute graph (${response.status}).`, response.status);
    return response.json();
  }
  async retry(): Promise<Response> {
    if (!this.saved.pending) throw new Error('No pending round.');
    return this.finish(this.saved.pending);
  }
  async action(action: 'spin' | 'buybonus' | 'next', multiplier: number): Promise<Response> {
    if (this.hasPending) return this.retry();
    if (action === 'next') return this.next();
    if (this.response.state.phase === 'FREE') throw new Error('Finish the remaining lives first.');
    if (this.replaying) {
      this.replaying = false;
      this.saved = new GraphGameApi().saved;
    }
    if (!Number.isSafeInteger(multiplier) || multiplier < 1 || multiplier > 10000) throw new Error('Invalid bet level.');
    const pending = { seed: 42, index: this.saved.nextIndex, multiplier, buy: action === 'buybonus', config: this.pinnedConfig(action === 'buybonus') };
    if (this.response.balance < config.baseCost * multiplier * (pending.buy ? config.buyCost : 1)) throw new Error('Insufficient virtual credits.');
    this.saved.pending = pending;
    this.persist();
    return this.finish(pending);
  }
  private async finish(pending: Pending): Promise<Response> {
    const raw = await this.request(pending);
    this.saved.frames = framesFromTrace(raw, pending.multiplier, pending.buy);
    this.saved.cursor = 0;
    this.saved.response = { ...this.response, balance: this.response.balance - config.baseCost * pending.multiplier * (pending.buy ? config.buyCost : 1),
      replay: { configHash: raw.configHash, seed: raw.seed, index: raw.roundIndex, buy: pending.buy },
      state: { ...this.response.state, triggeringMultiplier: pending.multiplier, roundWin: 0, bonusWin: 0 } };
    this.saved.nextIndex++;
    delete this.saved.pending;
    return this.next();
  }
  private next(): Response {
    const frame = this.saved.frames[this.saved.cursor];
    if (!frame) throw new Error('No remaining graph frames.');
    this.saved.cursor++;
    const base = this.saved.frames[0].type === 'BASE' ? this.saved.frames[0].win : 0;
    this.saved.response = { ...this.response, revision: this.response.revision + 1,
      balance: this.response.balance + (this.replaying ? 0 : frame.win), result: frame,
      state: { phase: frame.remaining > 0 ? 'FREE' : 'BASE', triggeringMultiplier: this.response.state.triggeringMultiplier,
        remaining: frame.remaining, multiplier: frame.multiplier, roundWin: frame.roundWin,
        bonusWin: frame.type === 'BASE' ? 0 : frame.roundWin - base, lastGrid: frame.finalGrid } };
    this.persist();
    return this.response;
  }
  async replay(hash: string, seed: number, index: number, buy: boolean): Promise<Response> {
    if (!Number.isSafeInteger(seed) || seed < 0 || !Number.isSafeInteger(index) || index < 0) throw new Error('Use non-negative integer seed and index.');
    const raw = await this.request({ seed, index, buy, multiplier: 1 });
    if (raw.configHash !== hash) throw new Error('Config hash mismatch. Load the original graph before replaying.');
    this.replaying = true;
    this.saved = { ...this.saved, frames: framesFromTrace(raw, 1, buy), cursor: 0,
      response: { ...this.response, replay: { configHash: hash, seed, index, buy }, state: { ...this.response.state, triggeringMultiplier: 1 } } };
    return this.next();
  }
}
