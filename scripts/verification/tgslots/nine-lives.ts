import { mt19937 } from '@tgslots/math/rng'
import type { Rng } from '@tgslots/math/rng/types'
import { Wager } from '@tgslots/slots-core/betting'
import { SYMBOLS, config } from '@tgslots/nine-lives/constants'
import { NineLivesSimulationMachine, simulationBetConfig } from '@tgslots/nine-lives/simulation-state-machine'
import { NineLivesModule } from '@tgslots-api/modules/nine-lives.module'
import type { LivesAction, LivesResult } from '@tgslots/nine-lives/types'
import { Mean, outcomeAt, recording, writeClientGolden, writeGolden, type Call, type ServedRound } from './shared'

const GOLDEN_ROUNDS = 60
const GOLDEN_SEED = 20260511
const SIMULATION_SEED = 7
const SIMULATED_ROUNDS = 40_000_000
const SIMULATED_BOUGHT_ROUNDS = 3_000_000
const CLIENT_SEED = 20260512
const CELLS = 30

/**
 * Everything the original draws, by the total weight it asks its generator for; the totals tell the draws apart.
 * A weight of 0 is no outcome at all, in the original as in the graph.
 */
const draws = [config.weights, config.bonusWeights, config.refillWeights, config.prizes.map(([, weight]) => weight!)]
  .map((weights) => weights.filter((weight) => weight > 0))
const total = (weights: readonly number[]) => weights.reduce((sum, weight) => sum + weight, 0)
if (new Set(draws.map(total)).size !== draws.length) throw new Error('Two draws of the original have the same total weight')

/** A group that paid: its symbol, its size, its pay in credits before the multiplier, and its cells in board order. */
interface Hit { symbol: number; size: number; pay: number; cells: number[] }
/** A spin as it landed: the board, the value of the coin on every cell (0 for none), and the multiplier it starts with. */
interface Spin { board: number[]; coins: number[]; multiplier: number; collected: number }
/** One step of a cascade that paid: which spin, the board that paid, the cells removed and the board after the fall. */
interface Step { spin: number; multiplier: number; win: number; board: number[]; removed: number[]; after: number[]; hits: Hit[] }
/**
 * `draws` are the graph's draw choices in its order: for a paid spin the thirty symbols, for a free spin the
 * thirty symbols and thirty prizes, and after every step that paid the symbols that dropped in.
 */
interface Round { draws: number[]; spins: Spin[]; steps: Step[]; baseWin: number; win: number; multiplier: number; triggered: boolean; capped: boolean }

/** The graph's draw choices for the numbers the original asked its generator for. */
const drawsOf = (calls: readonly Call[]) => calls.map(([lo, hi, value]) => {
  const weights = draws.find((candidate) => lo === 0 && hi === total(candidate))
  if (!weights) throw new Error(`The original asked for a number out of [${lo}, ${hi}), which is none of its draws`)
  return outcomeAt(weights, value)
})

function play(base: Rng, wager: Wager, buy: boolean): Round {
  const { rng, take } = recording(base)
  const machine = new NineLivesSimulationMachine(undefined, buy ? 'buy' : 'base')
  const round: Round = { draws: [], spins: [], steps: [], baseWin: 0, win: 0, multiplier: 1, triggered: buy, capped: false }
  for (let result = machine.spin(rng, wager) as ReturnType<typeof machine.next>; result; result = machine.next(rng)) {
    round.draws.push(...drawsOf(take()))
    round.win += result.win
    round.capped ||= result.capped
    // Buying the bonus is not a spin: nothing is drawn and nothing is paid.
    if (result.type === 'BUY') continue
    if (result.type === 'BASE') {
      round.baseWin = result.win
      round.triggered = result.bonusTriggered
    }
    const coins = Array.from({ length: CELLS }, () => 0)
    for (const coin of result.coins) coins[coin.position] = coin.value
    round.spins.push({ board: result.grid.flat(), coins, multiplier: round.multiplier, collected: result.collectionWin })
    for (const step of result.steps)
      round.steps.push({
        spin: round.spins.length - 1, multiplier: step.multiplier, win: step.win, board: step.before.flat(),
        removed: [...step.vanished].sort((a, b) => a - b), after: step.after.flat(),
        hits: step.hits.map((hit) => ({ symbol: hit.symbolId, size: hit.size, pay: hit.basePayout, cells: [...hit.positions].sort((a, b) => a - b) })),
      })
    // The multiplier the free spins have built so far; a paid spin leaves none behind.
    round.multiplier = result.type === 'FREE' ? result.multiplier : 1
  }
  return round
}

/** One round as the original game server serves it to its client: every request of the round and what it answered. */
function serve(base: Rng, multiplier: number, buy: boolean): ServedRound {
  const { rng, take } = recording(base)
  const module = new NineLivesModule()
  let state = module.defaultState(rng)
  const round: ServedRound = { graph: buy ? 'nine-lives.buy.json' : 'nine-lives.json', multiplier, draws: [], exchanges: [] }
  const request = (action: LivesAction, payload: { multiplier?: number }) => {
    const refused = module.validateAction(state, action, payload)
    if (refused) throw new Error(refused)
    const answer = module.execute(rng, state, action, payload)
    state = answer.state
    round.draws.push(...drawsOf(take()))
    round.exchanges.push({ action, payload, result: structuredClone(answer.result), state: structuredClone(state) })
  }
  request(buy ? 'buybonus' : 'spin', { multiplier })
  while (state.phase === 'FREE') request('next', {})
  return round
}

function reference(wager: Wager, buy: boolean, simulated: number) {
  // The first rounds as they come, then only rounds chosen for their feature: paid rounds that reached the free
  // spins, and bought rounds in which a spin cascaded four times or more.
  const deep = (round: Round) => round.spins.some((_, spin) => round.steps.filter((step) => step.spin === spin).length >= 4)
  const rounds: Round[] = []
  for (let rng = mt19937(GOLDEN_SEED); rounds.length < GOLDEN_ROUNDS; ) {
    const round = play(rng, wager, buy)
    if (rounds.length < GOLDEN_ROUNDS / 2 || (buy ? deep(round) : round.triggered)) rounds.push(round)
  }

  const cost = wager.totalWager
  const rtp = new Mean(), base = new Mean(), hit = new Mean(), trigger = new Mean(), bonus = new Mean(), steps = new Mean(), multiplier = new Mean(), capped = new Mean()
  for (let rng = mt19937(SIMULATION_SEED), i = 0; i < simulated; i++) {
    const round = play(rng, wager, buy)
    rtp.add(round.win / cost)
    base.add(round.baseWin / cost)
    hit.add(round.win > 0 ? 1 : 0)
    trigger.add(round.triggered ? 1 : 0)
    capped.add(round.capped ? 1 : 0)
    steps.add(round.steps.length)
    multiplier.add(round.multiplier)
    if (round.triggered) bonus.add((round.win - round.baseWin) / cost)
  }
  const simulation = {
    seed: SIMULATION_SEED, rounds: simulated, rtp: rtp.value, stdErr: rtp.stdErr, baseRtp: base.value, baseStdErr: base.stdErr,
    hitFrequency: hit.value, triggerFrequency: trigger.value, bonusWin: bonus.value, bonusWinStdErr: bonus.stdErr,
    stepsPerRound: steps.value, stepsStdErr: steps.stdErr, finalMultiplier: multiplier.value, finalMultiplierStdErr: multiplier.stdErr,
    cappedFrequency: capped.value,
  }
  return {
    source: `tgslots NineLivesSimulationMachine${buy ? ', bought bonus' : ''}; rounds from mt19937(${GOLDEN_SEED}), simulation from mt19937(${SIMULATION_SEED})`,
    // The tables of the original as it ships them, for the graph's own tables to be held against.
    totalBet: config.baseCost, roundCost: cost, symbols: SYMBOLS, config, simulation, rounds,
  }
}

/**
 * Arguments `base` or `buy` regenerate only that file; a number after it overrides how many rounds are simulated.
 * `client` writes the rounds the ported client is checked against instead.
 */
export default function main({ golden, args }: { golden: string; args: string[] }) {
  if (args[0] === 'client') {
    // Four paid rounds as they come, four that reached the free spins and four bought, at bet multipliers 1, 2 and 3.
    const rounds: ServedRound[] = []
    for (let rng = mt19937(CLIENT_SEED); rounds.length < 12; ) {
      const round = serve(rng, 1 + (rounds.length % 3), rounds.length >= 8)
      if (rounds.length < 4 || rounds.length >= 8 || (round.exchanges[0]!.result as LivesResult).bonusTriggered) rounds.push(round)
    }
    writeClientGolden(golden, 'nine-lives.client.golden.json', `tgslots NineLivesModule; rounds from mt19937(${CLIENT_SEED})`, rounds)
    return
  }
  const simulated = (fallback: number) => Number(args[1] ?? fallback)
  if (args[0] !== 'buy') writeGolden(golden, 'nine-lives.golden.json', reference(new Wager(1, simulationBetConfig('base')), false, simulated(SIMULATED_ROUNDS)))
  if (args[0] !== 'base') writeGolden(golden, 'nine-lives.buy.golden.json', reference(new Wager(1, simulationBetConfig('buy')), true, simulated(SIMULATED_BOUGHT_ROUNDS)))
}
