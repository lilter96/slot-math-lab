import { abortable } from './abort';
import type { GraphRound } from '../../lib/graphApi';
import { retryAfterMilliseconds } from '../../lib/realtime/httpFailure';
import type { PlayRound } from './server';

/** Posts one round to the play endpoint and gives back its response, whatever the status. */
export type PostRound = (body: unknown, signal?: AbortSignal) => Promise<Response>;

const ATTEMPTS = 4;
const SHORTEST_WAIT_MS = 250;
const LONGEST_WAIT_MS = 2000;

const pause = async (ms: number, signal?: AbortSignal) => {
  let timer!: ReturnType<typeof setTimeout>;
  try { await abortable(new Promise<void>(resolve => { timer = setTimeout(resolve, ms); }), signal); }
  finally { clearTimeout(timer); }
};

/**
 * Plays rounds through the play endpoint.
 *
 * The API turns a request away with 429 while it is computing for others, or when one address has asked too
 * often. A round is a function of its graph, its seed and its number, so asking for it again is safe. A short
 * wait is sat out here, a few times; a long one is not hidden from the player, who is told and may spin again.
 */
export function playThrough(post: PostRound, wait: (ms: number, signal?: AbortSignal) => Promise<void> = pause): PlayRound {
  return async (config, seed, roundIndex, signal) => {
    signal?.throwIfAborted();
    for (let attempt = 1; ; attempt++) {
      const response = await abortable(post({ config, seed, roundIndex, trace: true }, signal), signal);
      if (response.ok) {
        const round = await abortable(response.json(), signal) as GraphRound;
        return { trace: round.state, win: round.win };
      }
      const delay = Math.max(SHORTEST_WAIT_MS, retryAfterMilliseconds(response.headers.get('Retry-After')));
      if (response.status !== 429 || attempt === ATTEMPTS || delay > LONGEST_WAIT_MS) {
        const body = await response.json().catch(() => ({})) as { error?: string; title?: string };
        throw new Error(body.error ?? body.title ?? (response.status === 429 ? 'The game server is busy. Spin again shortly.' : `The round could not be played (${response.status})`));
      }
      await abortable(wait(delay, signal), signal);
    }
  };
}
