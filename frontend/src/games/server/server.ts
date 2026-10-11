import type { Game, Opening, Payload, PlayedRound, Step } from './game';

/** The body of a game request: what the ported client posted to its original server. */
export interface GameRequest {
  sessionId?: string;
  requestId?: string;
  expectedRevision?: number;
  payload?: Payload;
}

export interface GameResponse {
  status: number;
  body: unknown;
}

/** Plays round `round` of a graph for a seed, traced. */
export type PlayRound = (graph: Record<string, unknown>, seed: number, round: number) => Promise<PlayedRound>;

export interface SessionStore {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/** A round is the action that started it and what the player chose since: enough to lay it out again. */
interface RoundInProgress {
  index: number;
  opening: Opening<unknown>;
  inputs: Payload[];
}

/** The answer to a request that was carried out, kept so that the same request repeated gets the same answer. */
type Answered = [requestId: string, answer: { fingerprint: string; response: unknown }];

interface Session {
  gameId: string;
  state: unknown;
  seed: number;
  /** How many rounds the session has started; the next one is played as round `started` of the seed. */
  started: number;
  round: RoundInProgress | null;
  wallet?: { balance: number; revision: number; answered: Answered[] };
  touched: number;
}

const SESSION_TTL_MS = 60 * 60 * 1000;
const ANSWERS_KEPT = 16;

class Refusal extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

/**
 * The game server of the ported client, on top of the game graphs.
 *
 * It answers the requests the client made to its original server (`POST /game/<game>/<action>`) with the same
 * responses. A round is played by the game's graph in one call; what the client asks for one step at a time is
 * read from the trace of that round. A session keeps the seed and the number of the round instead of the
 * trace: the same seed and number give the same round again, so a round in progress survives a reload.
 */
export class GameServer {
  private readonly games = new Map<string, Game<unknown, unknown>>();
  private readonly laidOut = new Map<string, { index: number; steps: Step<unknown, unknown>[] }>();
  private readonly play: PlayRound;
  private readonly store: SessionStore;
  private readonly seed: () => number;
  private readonly now: () => number;

  constructor(play: PlayRound, store: SessionStore, seed: () => number, now: () => number = Date.now) {
    this.play = play;
    this.store = store;
    this.seed = seed;
    this.now = now;
  }

  register<State, Result>(game: Game<State, Result>): this {
    this.games.set(game.id, game as unknown as Game<unknown, unknown>);
    return this;
  }

  async handle(gameId: string, action: string, request: GameRequest): Promise<GameResponse> {
    try {
      return { status: 200, body: await this.answer(gameId, action, request) };
    } catch (error) {
      if (error instanceof Refusal) return { status: error.status, body: { error: error.message } };
      throw error;
    }
  }

  private async answer(gameId: string, action: string, request: GameRequest): Promise<unknown> {
    const game = this.games.get(gameId);
    if (!game) throw new Refusal(400, `Unknown game: ${gameId}`);
    const payload = request.payload ?? {};

    let sessionId = request.sessionId;
    let session: Session;
    if (sessionId) {
      const found = this.load(sessionId);
      if (!found) throw new Refusal(404, game.wallet?.messages.expired ?? 'Session not found or expired');
      if (found.gameId !== gameId) throw new Refusal(400, 'Session belongs to a different game');
      session = found;
    } else {
      if (game.wallet && action !== 'state') throw new Refusal(400, game.wallet.messages.noSession);
      sessionId = crypto.randomUUID();
      session = { gameId, state: game.initialState(), seed: this.seed(), started: 0, round: null, touched: this.now(),
        ...(game.wallet ? { wallet: { balance: game.wallet.initialBalance, revision: 0, answered: [] } } : {}) };
    }

    if (!game.wallet) {
      const refused = game.validate(session.state, action, payload);
      if (refused) throw new Refusal(400, refused);
      if (action === 'state') {
        this.save(sessionId, session);
        return { sessionId, state: session.state };
      }
      const reply = await this.take(game, sessionId, session, action, payload);
      this.save(sessionId, session);
      return { sessionId, result: reply.result, state: reply.state };
    }

    const wallet = session.wallet!;
    if (action === 'state') {
      this.save(sessionId, session);
      return { sessionId, state: session.state, balance: wallet.balance, revision: wallet.revision };
    }
    const { requestId, expectedRevision } = request;
    if (!requestId || !game.wallet.identified(requestId, expectedRevision)) throw new Refusal(400, game.wallet.messages.identity);
    const fingerprint = JSON.stringify(game.wallet.fingerprint(action, payload, expectedRevision!));
    const answered = wallet.answered.find(([id]) => id === requestId)?.[1];
    if (answered) {
      if (answered.fingerprint !== fingerprint) throw new Refusal(409, game.wallet.messages.reused);
      return answered.response;
    }
    if (expectedRevision !== wallet.revision) throw new Refusal(409, game.wallet.messages.stale);
    const refused = game.validate(session.state, action, payload);
    if (refused) throw new Refusal(400, refused);
    const cost = game.wallet.cost(action, session.state, payload);
    if (wallet.balance < cost) throw new Refusal(400, 'Insufficient demo credits');
    const reply = await this.take(game, sessionId, session, action, payload);
    wallet.balance += game.wallet.award(reply.result) - cost;
    wallet.revision++;
    const response = { sessionId, state: reply.state, result: reply.result, balance: wallet.balance, revision: wallet.revision };
    wallet.answered = [...wallet.answered, [requestId, { fingerprint, response }] satisfies Answered].slice(-ANSWERS_KEPT);
    this.save(sessionId, session);
    return response;
  }

  /** Takes one action in the session: starts a round, or gives the next answer of the round in progress. */
  private async take(game: Game<unknown, unknown>, sessionId: string, session: Session, action: string, payload: Payload) {
    const opening: Opening<unknown> = { action, payload, state: session.state };
    if (game.graph(opening)) {
      const index = session.started;
      const steps = await this.layOut(game, sessionId, session.seed, { index, opening, inputs: [] });
      const reply = steps[0].reply(payload, session.state);
      session.started = index + 1;
      session.round = { index, opening, inputs: [] };
      session.state = reply.state;
      return reply;
    }
    const round = session.round;
    if (!round) throw new Refusal(400, 'No round in progress');
    const steps = await this.layOut(game, sessionId, session.seed, round);
    const step = steps[round.inputs.length + 1];
    if (step?.action !== action) throw new Refusal(400, `The round in progress does not continue with ${action}`);
    const reply = step.reply(payload, session.state);
    session.round = { ...round, inputs: [...round.inputs, payload] };
    session.state = reply.state;
    return reply;
  }

  /** The answers of a round. They are kept while the page lives and played again from the seed after a reload. */
  private async layOut(game: Game<unknown, unknown>, sessionId: string, seed: number, round: RoundInProgress) {
    const kept = this.laidOut.get(sessionId);
    if (kept?.index === round.index) return kept.steps;
    const steps = game.steps(await this.play(game.graph(round.opening)!, seed, round.index), round.opening);
    this.laidOut.set(sessionId, { index: round.index, steps });
    return steps;
  }

  private key(sessionId: string) {
    return `slot-math-lab:game-session:${sessionId}`;
  }

  private load(sessionId: string): Session | null {
    let session: Session | null = null;
    try {
      session = JSON.parse(this.store.getItem(this.key(sessionId)) ?? 'null') as Session | null;
    } catch {
      /* A session that cannot be read is a session that is gone. */
    }
    if (!session) return null;
    if (this.now() - session.touched > SESSION_TTL_MS) {
      this.store.removeItem(this.key(sessionId));
      return null;
    }
    return session;
  }

  private save(sessionId: string, session: Session): void {
    session.touched = this.now();
    this.store.setItem(this.key(sessionId), JSON.stringify(session));
  }
}
