import { ancientDragon } from './ancient-dragon';
import { leMilitare } from './le-militare';
import { nineLives } from './nine-lives';
import { playThrough } from './play';
import { woodlandWhisper } from './woodland-whisper';
import { x7Club } from './x7-club';
import { GameServer, type GameRequest, type SessionStore } from './server';

/** Sessions live in the browser's storage; without one they last as long as the page. */
function sessionStore(): SessionStore {
  try {
    // Without cross-tab locking, keep the session in this page rather than share an unsafe wallet.
    if (!navigator.locks) throw new Error('Cross-tab session locking is unavailable');
    localStorage.setItem('slot-math-lab:game-session', '');
    return localStorage;
  } catch {
    const kept = new Map<string, string>();
    return { getItem: key => kept.get(key) ?? null, setItem: (key, value) => void kept.set(key, value), removeItem: key => void kept.delete(key) };
  }
}

const server = new GameServer(
  playThrough((body, signal) => fetch((import.meta.env.VITE_API_URL ?? '') + '/api/play/round', { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal })),
  sessionStore(),
  () => crypto.getRandomValues(new Uint32Array(1))[0],
).register(ancientDragon).register(woodlandWhisper).register(x7Club).register(nineLives).register(leMilitare);

/**
 * Stands where the ported client called `fetch` for its game server: the same URL (`…/game/<game>/<action>`), the
 * same JSON body, the same responses. The rounds are played by the game graphs through `/api/play/round`.
 */
export async function serveGame(url: string, init: RequestInit & { body: string }): Promise<Response> {
  const [, gameId, action] = /\/game\/([^/]+)\/([^/?]+)$/.exec(url) ?? [];
  if (!gameId) return Response.json({ error: `Not a game request: ${url}` }, { status: 404 });
  init.signal?.throwIfAborted();
  try {
    const { status, body } = await server.handle(gameId, action, JSON.parse(init.body) as GameRequest, init.signal ?? undefined);
    return Response.json(body, { status });
  } catch (error) {
    if (init.signal?.aborted) throw init.signal.reason;
    // The graph could not be played (the API is busy or away). Nothing was committed: the client may ask again.
    return Response.json({ error: error instanceof Error ? error.message : 'The game graph could not be played' }, { status: 503 });
  }
}
