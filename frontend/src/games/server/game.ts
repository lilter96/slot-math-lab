/**
 * The final state of one round a game graph played with tracing on, as `/api/play/round` returns it. The graph
 * records there what happened in the round: the boards, the wins, the draws of its features.
 */
export type Trace = Readonly<Record<string, unknown>>;

/** A round a game graph played: its trace, and what it paid as a multiple of what the round cost. */
export interface PlayedRound {
  trace: Trace;
  win: number;
}

export type Payload = Readonly<Record<string, unknown>>;

/** What a game answers to one action: what happened, and the state the session is left in. */
export interface Reply<State, Result> {
  state: State;
  result: Result;
}

/** One answer of a round, in the order the client asks for them. It can depend on what the player chose. */
export interface Step<State, Result> {
  action: string;
  reply(payload: Payload, state: State): Reply<State, Result>;
}

/** The credits of a round as a client counts them: whole, at the bet multiplier the round was played at. */
export function credits(round: PlayedRound, multiplier: number): number {
  return Math.round(round.win * number(round.trace.roundCost) * multiplier);
}

/** The action that started a round, and the state it started from. */
export interface Opening<State> {
  action: string;
  payload: Payload;
  state: State;
}

/** The session wallet of a game whose balance the server keeps, with the messages of its original server. */
export interface Wallet<State, Result> {
  initialBalance: number;
  cost(action: string, state: State, payload: Payload): number;
  award(result: Result): number;
  /** Whether a request says which command it is: its own identity, and the revision of the session it was made for. */
  identified(requestId: string | undefined, expectedRevision: number | undefined): boolean;
  /** What identifies a command, so that a repeated request can be told from a reused identity. */
  fingerprint(action: string, payload: Payload, expectedRevision: number): unknown;
  messages: { noSession: string; expired: string; identity: string; reused: string; stale: string };
}

/**
 * One game of the ported client, as its original server presented it: the same actions, results and states.
 * There is no game math here. An action that starts a round names the graph that plays it; the whole round is
 * played by that graph in one go, and `steps` only lays the trace it left out as the answers the client asks
 * for one at a time (spin, then each free spin, each pick, each respin).
 */
export interface Game<State, Result> {
  readonly id: string;
  readonly wallet?: Wallet<State, Result>;
  initialState(): State;
  /** Why the action cannot be taken in this state, in the words of the original server; null when it can. */
  validate(state: State, action: string, payload: Payload): string | null;
  /** The graph that plays the round this action starts; null for an action that continues the round in progress. */
  graph(opening: Opening<State>): Record<string, unknown> | null;
  steps(round: PlayedRound, opening: Opening<State>): Step<State, Result>[];
}

/**
 * A number of a trace. The API writes whole numbers as strings and fractions as a numerator and a denominator;
 * the canonical form of a fraction is the string `"n/d"`.
 */
export function number(value: unknown): number {
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && /^-?\d+$/.test(value)) return Number(value);
  if (typeof value === 'string' && /^-?\d+\/\d+$/.test(value)) return Number(value.split('/')[0]) / Number(value.split('/')[1]);
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (value !== null && typeof value === 'object' && 'numerator' in value && 'denominator' in value)
    return Number(value.numerator) / Number(value.denominator);
  throw new Error(`Not a number in the round's trace: ${JSON.stringify(value)}`);
}

export const numbers = (value: unknown): number[] => {
  if (!Array.isArray(value)) throw new Error(`Not a list in the round's trace: ${JSON.stringify(value)}`);
  return value.map(number);
};

export const lists = (value: unknown): number[][] => {
  if (!Array.isArray(value)) throw new Error(`Not a list in the round's trace: ${JSON.stringify(value)}`);
  return value.map(numbers);
};

/** A board the graphs keep as one list, reel after reel, as the reels the client shows. */
export const reels = (cells: readonly number[], rows: number): number[][] =>
  Array.from({ length: cells.length / rows }, (_, reel) => cells.slice(reel * rows, reel * rows + rows));

/** The same board as the rows the client shows, for a game whose client lays its grid out row by row. */
export const rowsOf = (grid: readonly (readonly number[])[]): number[][] =>
  grid[0].map((_, row) => grid.map(reel => reel[row]));

/**
 * Chances that decide only how a result is shown, never what it is: where the cards of a bonus lie, which of two
 * equal cards turns first. They follow from what is shown, so the same round is shown the same way again.
 */
export function showing(...of: readonly unknown[]): (below: number) => number {
  let seed = 2166136261;
  for (const char of JSON.stringify(of)) seed = Math.imul(seed ^ char.charCodeAt(0), 16777619);
  return below => {
    seed = seed + 0x6d2b79f5 | 0;
    let mixed = Math.imul(seed ^ seed >>> 15, 1 | seed);
    mixed = mixed + Math.imul(mixed ^ mixed >>> 7, 61 | mixed) ^ mixed;
    return Math.floor(((mixed ^ mixed >>> 14) >>> 0) / 4294967296 * below);
  };
}

/** Keeps a graph that has been built: a game graph is large, and the same one plays every round of its kind. */
export function remembered<Key, Value>(build: (key: Key) => Value): (key: Key) => Value {
  const built = new Map<Key, Value>();
  return key => {
    if (!built.has(key)) built.set(key, build(key));
    return built.get(key)!;
  };
}
