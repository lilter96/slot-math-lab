import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ancientDragon } from '../src/games/server/ancient-dragon';
import type { LeMilitareResult } from '../src/games/contracts/le-militare';
import type { LivesResult } from '../src/games/contracts/nine-lives';
import type { ClubResult } from '../src/games/contracts/x7-club';
import type { WoodlandWhisperSerializedState } from '../src/games/contracts/shared-contracts/states';
import { number, type Game, type Payload } from '../src/games/server/game';
import { leMilitare } from '../src/games/server/le-militare';
import { nineLives } from '../src/games/server/nine-lives';
import { woodlandWhisper } from '../src/games/server/woodland-whisper';
import { x7Club } from '../src/games/server/x7-club';

// The ported game client is served from the traces of the game graphs. `scripts/verification/tgslots` drove the
// original game server through a few rounds of every game and wrote down the draws and its answers
// (`*.client.golden.json`); the backend test GameClientTraceTests replays those draws through the graphs and
// keeps what each round wrote to its state (`*.client.traces.json`). Here every game is asked the same
// requests over those traces, and must answer what the original server answered.
const data = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../backend/SlotMath.Core.Tests/TestData/Games');
const read = (file: string) => JSON.parse(fs.readFileSync(path.join(data, file), 'utf8'));

interface Exchange { action: string; payload: Payload; result: unknown; state: unknown }
interface ServedRound { graph: string; multiplier: number; exchanges: Exchange[] }

/** Where two values differ, or null when they are the same; numbers may differ by a rounding of the last digits. */
function difference(actual: unknown, expected: unknown, where = '$'): string | null {
  if (typeof actual === 'number' && typeof expected === 'number')
    return Math.abs(actual - expected) <= 1e-9 * Math.max(1, Math.abs(expected)) ? null : `${where}: ${actual} instead of ${expected}`;
  if (actual === null || expected === null || typeof actual !== 'object' || typeof expected !== 'object')
    return actual === expected ? null : `${where}: ${JSON.stringify(actual)} instead of ${JSON.stringify(expected)}`;
  if (Array.isArray(actual) !== Array.isArray(expected)) return `${where}: one is a list, the other is not`;
  const keys = [...new Set([...Object.keys(actual), ...Object.keys(expected)])];
  for (const key of keys) {
    const found = difference((actual as Record<string, unknown>)[key], (expected as Record<string, unknown>)[key], `${where}.${key}`);
    if (found) return found;
  }
  return null;
}

interface Answer { result: unknown; state: unknown }

/**
 * Requests that only show a result the round already has, like the cards of a pick bonus: how many there are and
 * what each shows is drawn for the show, by the original server and by this one alike, so the two are not compared
 * request by request. Each run of them has to be a show the client can play, and to end where the original's ended.
 */
interface Show {
  action: string;
  payload(turn: number): Payload;
  /** An answer without what was drawn for the show. */
  outcome(answer: Answer): unknown;
  /** Fails unless the answers of one run make a show the client can play. */
  playable(answers: (Answer & { payload: Payload })[]): void;
}

/**
 * Asks a game the requests of the original server's rounds over the graph's traces, and compares the answers.
 * `creditsPerUnit` is how many credits of the graph one unit of the client's money is; `expected` puts an answer of
 * the original into the form it is compared in, where the order it lists something in means nothing.
 */
function served<State, Result>(game: Game<State, Result>, file: string,
  { show, creditsPerUnit = 1, expected = answer => answer }: { show?: Show; creditsPerUnit?: number; expected?: (original: Answer) => Answer } = {}) {
  const rounds: ServedRound[] = read(`${file}.client.golden.json`).rounds;
  const traces: { win: string; trace: Record<string, unknown> }[] = read(`${file}.client.traces.json`);
  expect(traces).toHaveLength(rounds.length);
  const outcome = ({ result, state }: Answer) => show ? show.outcome({ result, state }) : { result, state };
  const fixtures = new Map<string, { initialState: Record<string, unknown> }>();
  rounds.forEach((round, index) => {
    const [first, ...rest] = round.exchanges;
    let state = game.initialState();
    const opening = { action: first.action, payload: first.payload, state };
    // The graph the game plays this round on is the very graph the backend replayed the round through.
    if (!fixtures.has(round.graph)) fixtures.set(round.graph, read(round.graph));
    expect(JSON.parse(JSON.stringify(game.graph(opening))), `round ${index} is played on ${round.graph}`).toEqual(fixtures.get(round.graph));
    for (const exchange of rest) expect(game.graph({ ...exchange, state }), `${exchange.action} continues a round`).toBeNull();

    // The play endpoint returns the whole state of the round: what the graph started with, and what the round wrote.
    const played = { win: number(traces[index].win), trace: { ...fixtures.get(round.graph)!.initialState, ...traces[index].trace } };
    const steps = game.steps(played, opening);
    let paid = 0;
    const ask = (step: number, payload: Payload): Answer => {
      const where = `round ${index}, step ${step}`;
      expect(steps[step], where).toBeDefined();
      expect(game.validate(state, steps[step].action, payload), where).toBeNull();
      const reply = steps[step].reply(payload, state);
      state = reply.state;
      paid += (reply.result as { win: number }).win;
      return JSON.parse(JSON.stringify(reply));
    };
    let step = 0;
    for (let at = 0; at < round.exchanges.length; at++) {
      const where = `round ${index}, request ${at} (${round.exchanges[at].action})`;
      let answer: Answer;
      if (round.exchanges[at].action === show?.action) {
        const answers: (Answer & { payload: Payload })[] = [];
        while (steps[step]?.action === show.action) {
          const payload = show.payload(answers.length);
          answers.push({ payload, ...ask(step++, payload) });
        }
        expect(answers.length, where).toBeGreaterThan(0);
        show.playable(answers);
        while (round.exchanges[at + 1]?.action === show.action) at++;
        answer = answers[answers.length - 1];
      } else {
        expect(steps[step]?.action, where).toBe(round.exchanges[at].action);
        answer = ask(step++, round.exchanges[at].payload);
      }
      expect(difference(outcome(answer), outcome(expected(round.exchanges[at]))), where).toBeNull();
    }
    expect(step, `round ${index} has no answers left over`).toBe(steps.length);
    // Whatever the answers say was won, together they pay what the graph paid for the round.
    expect(paid, `round ${index} pays what its graph paid`).toBeCloseTo(played.win * number(played.trace.roundCost) * round.multiplier / creditsPerUnit, 6);
  });
  return rounds;
}

test('Ancient Dragon answers its client as the original server did', () => {
  const rounds = served(ancientDragon, 'ancient-dragon');
  expect(rounds.some(round => round.exchanges.length > 11), 'a retrigger is covered').toBe(true);
  expect(new Set(rounds.map(round => round.multiplier)).size, 'several bet multipliers are covered').toBeGreaterThan(1);
});

test('Woodland Whisper answers its client as the original server did, and its pick bonuses can be played', () => {
  type Bonus = WoodlandWhisperSerializedState['pickBonus'];
  type Pick = { pick?: { userIndex: number; revealedIndex: number; value: number; isMatch: boolean; board: number[]; picks: number[] } };
  const sorted = (cards: number[]) => [...cards].sort((a, b) => a - b);
  let bonuses = 0;
  const rounds = served(woodlandWhisper, 'woodland-whisper', { show: {
    action: 'pick',
    payload: turn => ({ userIndex: (turn * 7) % 20 }),
    // Where the cards lie and in which order they turn is drawn for the show; which cards there are, what the
    // bonus awards and everything it leaves behind is not.
    outcome({ result, state }) {
      const { pick, ...shown } = result as Pick & Record<string, unknown>;
      const { pickBonus, ...session } = state as { pickBonus: Bonus };
      return {
        result: { ...shown, ...(pick ? { pick: { value: pick.value, isMatch: pick.isMatch, board: sorted(pick.board) } } : {}) },
        state: { ...session, pickBonus: pickBonus && { board: sorted(pickBonus.board), winValue: pickBonus.winValue, triggeringMultiplier: pickBonus.triggeringMultiplier } },
      };
    },
    playable(answers) {
      bonuses++;
      const turned = answers.map(answer => (answer.result as Required<Pick>).pick);
      const board = turned[0].board;
      turned.forEach((pick, turn) => {
        expect(pick.board).toEqual(board);
        expect(pick.value).toBe(board[pick.revealedIndex]);
        expect(pick.userIndex).toBe(answers[turn].payload.userIndex);
        expect(pick.picks).toEqual(answers.slice(0, turn + 1).map(answer => answer.payload.userIndex));
        // The bonus ends with the second card of the award and not before.
        expect(pick.isMatch).toBe(turn === turned.length - 1);
      });
      expect(new Set(turned.map(pick => pick.revealedIndex)).size, 'no card turns twice').toBe(turned.length);
      const last = turned[turned.length - 1].value, amounts = turned.map(pick => pick.value);
      expect(amounts.filter(amount => amount === last)).toHaveLength(2);
      expect(new Set(amounts).size, 'only the award comes up twice').toBe(amounts.length - 1);
      const left = (answers[answers.length - 1].state as { pickBonus: Bonus }).pickBonus;
      expect(left).toBeNull();
      answers.slice(0, -1).forEach((answer, turn) => {
        const bonus = (answer.state as { pickBonus: NonNullable<Bonus> }).pickBonus;
        expect(bonus.currentPickIndex).toBe(turn + 1);
        expect(bonus.revealedValues).toEqual(amounts.slice(0, turn + 1));
        expect(bonus.winValue).toBe(last);
      });
    },
  } });
  expect(bonuses, 'pick bonuses are covered').toBeGreaterThan(8);
  expect(rounds.some(round => round.graph === 'woodland-whisper.buy.json'), 'the bought feature is covered').toBe(true);
  expect(rounds.some(round => round.exchanges.filter(exchange => exchange.action === 'freespin').length > 20), 'a retrigger is covered').toBe(true);
});

test('X7 Club answers its client as the original server did', () => {
  const rounds = served(x7Club, 'x7-club');
  const results = rounds.flatMap(round => round.exchanges.map(exchange => exchange.result as ClubResult));
  expect(new Set(results.flatMap(result => result.boost ? [result.boost.kind] : [])).size, 'several kinds of pull are covered').toBeGreaterThan(1);
  expect(results.some(result => result.type === 'BUY'), 'the bought bonus is covered').toBe(true);
  expect(results.some(result => result.type === 'BASE' && result.bonusTriggered), 'a triggered Hold & Spin is covered').toBe(true);
});

// The original lists the cells of a group, and the cells a step removes, in the order its flood fill came upon
// them. The client lights them all at once, so the games list them in board order, and they are compared as sets.
const inBoardOrder = (cells: readonly number[]) => [...cells].sort((a, b) => a - b);

test('Nine Lives answers its client as the original server did', () => {
  const rounds = served(nineLives, 'nine-lives', { expected: ({ result, state }) => ({ state, result: { ...(result as LivesResult),
    steps: (result as LivesResult).steps.map(step => ({ ...step, vanished: inBoardOrder(step.vanished),
      hits: step.hits.map(hit => ({ ...hit, positions: inBoardOrder(hit.positions) })) })) } }) });
  const results = rounds.flatMap(round => round.exchanges.map(exchange => exchange.result as LivesResult));
  expect(results.some(result => result.type === 'BASE' && result.bonusTriggered), 'triggered free spins are covered').toBe(true);
  expect(results.some(result => result.type === 'BUY'), 'the bought bonus is covered').toBe(true);
  expect(results.some(result => result.coins.length > 0 && result.steps.length > 1), 'collected coins and cascades are covered').toBe(true);
  expect(Math.max(...results.map(result => result.multiplier)), 'a multiplier that grew is covered').toBeGreaterThan(3);
});

test('Le Militare answers its client as the original server did', () => {
  const rounds = served(leMilitare, 'le-militare', {
    // The client counts in bets, the graphs in credits of a bet of 100.
    creditsPerUnit: 100,
    expected: ({ result, state }) => ({ state, result: { ...(result as LeMilitareResult),
      steps: (result as LeMilitareResult).steps.map(step => ({ ...step, vanishedPositions: inBoardOrder(step.vanishedPositions),
        hits: step.hits.map(hit => ({ ...hit, positions: inBoardOrder(hit.positions) })) })) } }),
  });
  const results = rounds.flatMap(round => round.exchanges.map(exchange => exchange.result as LeMilitareResult));
  const steps = results.flatMap(result => result.steps);
  expect(new Set(rounds.map(round => round.graph)).size, 'every kind of round and every mode is covered').toBe(8);
  expect(results.some(result => result.type === 'BASE' && result.state.freeSpinsLeft > 0), 'triggered free spins are covered').toBe(true);
  expect(results.some(result => result.type === 'FREE' && result.retriggered), 'a retrigger is covered').toBe(true);
  expect(results.some(result => result.type === 'BASE' && (result.airRaid?.placements.length ?? 0) > 0), 'an air raid that left wilds is covered').toBe(true);
  expect(steps.some(step => step.activations.length > 0 && step.shootdowns.length > 0), 'a reel that armed and shot planes down is covered').toBe(true);
  expect(steps.some(step => step.hits.length > 0 && step.stickyWildPositions.length > 0), 'held cells that stayed through a step are covered').toBe(true);
  expect(rounds.some(round => (round.exchanges.at(-1)!.state as { freeSpins: { armedReels: number[] } | null }).freeSpins?.armedReels.length), 'reels armed in the free spins are covered').toBe(true);
});

test('Le Militare plays every kind of round in every mode on the graph of that round', () => {
  const prices: Record<string, Record<string, number>> = read('le-militare.prices.json');
  const starters: [feature: string, action: string, payload: Payload][] = [
    ['spin', 'spin', {}], ['chance', 'chancespin', {}], ['air-raid', 'airraidspin', {}],
    ['standard', 'buybonus', {}], ['standard', 'buybonus', { option: 'standard' }], ['elite', 'buybonus', { option: 'elite' }], ['super', 'buybonus', { option: 'super' }],
  ];
  for (const mode of Object.keys(prices)) for (const [feature, action, payload] of starters) {
    // A round is played in the mode its request names, or else in the mode the session was last played in.
    for (const opening of [{ action, payload: { ...payload, multiplier: 1, mode }, state: leMilitare.initialState() },
      { action, payload: { ...payload, multiplier: 1 }, state: { ...leMilitare.initialState(), mode: mode as 'assault' } }]) {
      const graph = leMilitare.graph(opening) as { id: string; initialState: { roundCost: number } };
      expect(graph.id, `${mode} ${feature}`).toBe(['le-militare', ...(mode === 'assault' ? [] : [mode]), ...(feature === 'spin' ? [] : [feature])].join('-'));
      expect(graph.initialState.roundCost, `${mode} ${feature}`).toBe(prices[mode][feature]);
    }
  }
  expect(leMilitare.graph({ action: 'freespin', payload: {}, state: leMilitare.initialState() })).toBeNull();
});
