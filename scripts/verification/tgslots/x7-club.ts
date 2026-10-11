import { mt19937 } from '@tgslots/math/rng'
import type { Rng } from '@tgslots/math/rng/types'
import { Wager } from '@tgslots/slots-core/betting'
import { BET_CONFIG, config } from '@tgslots/x7-club/constants'
import { X7ClubMachine } from '@tgslots/x7-club/machine'
import type { ClubResult } from '@tgslots/x7-club/types'
import { X7ClubSimulationMachine, simulationBetConfig } from '@tgslots/x7-club/simulation-state-machine'
import { Mean, outcomeAt, recording, writeClientGolden, writeGolden, type ServedRound } from './shared'

const GOLDEN_ROUNDS = 60
const GOLDEN_SEED = 20260511
const SIMULATION_SEED = 7
const SIMULATED_ROUNDS = 40_000_000
const SIMULATED_BOUGHT_ROUNDS = 2_000_000
const CLIENT_SEED = 20260512
const CELLS = 15

/** Everything the original draws, by the total weight it asks its generator for; the totals tell the draws apart. */
const draws = [
  config.weights,
  config.prizes.map(([, , weight]) => Number(weight)),
  [config.respinHitWeight, config.respinMissWeight],
  config.boostWeights.map(([, weight]) => Number(weight)),
]
const total = (weights: readonly number[]) => weights.reduce((sum, weight) => sum + weight, 0)
if (new Set(draws.map(total)).size !== draws.length) throw new Error('Two draws of the original have the same total weight')

/** One step of the Hold & Spin: a respin (column −1), or a pull of the boost of a column, named by its kind. */
interface Step { event: string; column: number; coins: number[]; respins: number }
/**
 * `draws` are the graph's draw choices in its order: a paid spin's fifteen symbols and fifteen prizes, then for
 * every respin each cell's hit or miss and its prize, and for every pull the kind that came up. `coins` are the
 * coin values on the fifteen cells, 0 where there is none.
 */
interface Round { draws: number[]; board: number[] | null; lineWin: number; coins: number[]; steps: Step[]; win: number; capped: boolean }

const cells = (coins: readonly { position: number; value: number }[]) => {
  const values = Array.from({ length: CELLS }, () => 0)
  for (const coin of coins) values[coin.position] = coin.value
  return values
}

function play(base: Rng, wager: Wager, buy: boolean): Round {
  const { rng, take } = recording(base)
  const machine = new X7ClubSimulationMachine(undefined, buy ? 'buy' : 'base')
  const first = machine.spin(rng, wager)
  const round: Round = {
    draws: [], board: buy ? null : first.grid.flat(), lineWin: first.win, coins: cells(first.coins), steps: [], win: first.win, capped: first.capped,
  }
  for (let result: ReturnType<typeof machine.next> = first; result; result = machine.next(rng)) {
    for (const [lo, hi, value] of take()) {
      const weights = draws.find((candidate) => lo === 0 && hi === total(candidate))
      if (!weights) throw new Error(`The original asked for a number out of [${lo}, ${hi}), which is none of its draws`)
      round.draws.push(outcomeAt(weights, value))
    }
    if (result === first) continue
    round.steps.push({ event: result.boost?.kind ?? 'respin', column: result.boost?.column ?? -1, coins: cells(result.coins), respins: result.respins })
    round.win += result.win
    round.capped ||= result.capped
  }
  return round
}

/** One round as the original game server serves it to the client: a spin or a bought bonus, then a request per step. */
function serve(base: Rng, multiplier: number, buy: boolean): ServedRound {
  const { rng, take } = recording(base)
  const machine = new X7ClubMachine()
  const wager = new Wager(multiplier, BET_CONFIG)
  const round: ServedRound = { graph: buy ? 'x7-club.buy.json' : 'x7-club.json', multiplier, draws: [], exchanges: [] }
  const answered = (action: string, payload: Record<string, unknown>, result: ClubResult) => {
    for (const [lo, hi, value] of take()) {
      const weights = draws.find((candidate) => lo === 0 && hi === total(candidate))
      if (!weights) throw new Error(`The original asked for a number out of [${lo}, ${hi}), which is none of its draws`)
      round.draws.push(outcomeAt(weights, value))
    }
    round.exchanges.push({ action, payload, result: structuredClone(result), state: structuredClone(machine.state) })
  }
  if (buy) answered('buybonus', { multiplier }, machine.buyBonus(wager))
  else answered('spin', { multiplier }, machine.spin(rng, wager))
  while (machine.state.phase !== 'BASE') answered('next', {}, machine.next(rng)!)
  return round
}

function reference(wager: Wager, buy: boolean, simulated: number) {
  // The first rounds as they come, then only rounds whose Hold & Spin boosted a column, so that the feature is covered in depth.
  const rounds: Round[] = []
  for (let rng = mt19937(GOLDEN_SEED); rounds.length < GOLDEN_ROUNDS; ) {
    const round = play(rng, wager, buy)
    if (rounds.length < GOLDEN_ROUNDS / 2 || round.steps.filter((step) => step.column >= 0).length > (buy ? 3 : 0)) rounds.push(round)
  }

  const cost = wager.totalWager
  const rtp = new Mean(), lines = new Mean(), hit = new Mean(), trigger = new Mean(), bonus = new Mean(), respins = new Mean(), pulls = new Mean(), capped = new Mean()
  for (let rng = mt19937(SIMULATION_SEED), i = 0; i < simulated; i++) {
    const round = play(rng, wager, buy)
    rtp.add(round.win / cost)
    lines.add(round.lineWin / cost)
    hit.add(round.win > 0 ? 1 : 0)
    trigger.add(round.steps.length > 0 ? 1 : 0)
    capped.add(round.capped ? 1 : 0)
    if (round.steps.length > 0) {
      bonus.add((round.win - round.lineWin) / cost)
      respins.add(round.steps.filter((step) => step.column < 0).length)
      pulls.add(round.steps.filter((step) => step.column >= 0).length)
    }
  }
  const simulation = {
    seed: SIMULATION_SEED, rounds: simulated, rtp: rtp.value, stdErr: rtp.stdErr, lineRtp: lines.value, hitFrequency: hit.value,
    triggerFrequency: trigger.value, bonusWin: bonus.value, bonusWinStdErr: bonus.stdErr, respinsPerBonus: respins.value,
    pullsPerBonus: pulls.value, cappedFrequency: capped.value,
  }
  return {
    source: `tgslots X7ClubSimulationMachine${buy ? ', bought bonus' : ''}; rounds from mt19937(${GOLDEN_SEED}), simulation from mt19937(${SIMULATION_SEED})`,
    totalBet: config.baseCost, roundCost: cost, simulation, rounds,
  }
}

/** Arguments `base` or `buy` regenerate only that file; without one, both. `client` writes the server's answers. */
export default function main({ golden, args }: { golden: string; args: string[] }) {
  if (args[0] === 'client') {
    // Four paid rounds as they come, four whose Hold & Spin boosted a column and four bought, at bet multipliers 1, 2 and 3.
    const rounds: ServedRound[] = []
    for (let rng = mt19937(CLIENT_SEED); rounds.length < 12; ) {
      const round = serve(rng, 1 + (rounds.length % 3), rounds.length >= 8)
      const boosts = round.exchanges.filter((exchange) => (exchange.result as ClubResult).boost).length
      if (rounds.length < 4 || boosts > (rounds.length >= 8 ? 3 : 0)) rounds.push(round)
    }
    writeClientGolden(golden, 'x7-club.client.golden.json', `tgslots X7ClubMachine; rounds from mt19937(${CLIENT_SEED})`, rounds)
    return
  }
  if (args[0] !== 'buy') writeGolden(golden, 'x7-club.golden.json', reference(new Wager(1, simulationBetConfig('base')), false, SIMULATED_ROUNDS))
  if (args[0] !== 'base') writeGolden(golden, 'x7-club.buy.golden.json', reference(new Wager(1, simulationBetConfig('buy')), true, SIMULATED_BOUGHT_ROUNDS))
}
