import { graphRequest, type GraphRound } from '../../lib/graphApi';
import { ancientDragon } from './ancient-dragon';
import { leMilitare } from './le-militare';
import { nineLives } from './nine-lives';
import { woodlandWhisper } from './woodland-whisper';
import { x7Club } from './x7-club';
import { GameServer, type GameRequest, type SessionStore } from './server';

/** Sessions live in the browser's storage; without one they last as long as the page. */
function sessionStore(): SessionStore {
  try {
    localStorage.setItem('slot-math-lab:game-session', '');
    return localStorage;
  } catch {
    const kept = new Map<string, string>();
    return { getItem: key => kept.get(key) ?? null, setItem: (key, value) => void kept.set(key, value), removeItem: key => void kept.delete(key) };
  }
}

const server = new GameServer(
  async (config, seed, roundIndex) => {
    const round = await graphRequest<GraphRound>('play/round', { config, seed, roundIndex, trace: true });
    return { trace: round.state, win: round.win };
  },
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
  try {
    const { status, body } = await server.handle(gameId, action, JSON.parse(init.body) as GameRequest);
    return Response.json(body, { status });
  } catch (error) {
    // The graph could not be played (the API is busy or away). Nothing was committed: the client may ask again.
    return Response.json({ error: error instanceof Error ? error.message : 'The game graph could not be played' }, { status: 503 });
  }
}
