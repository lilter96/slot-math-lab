import { mt19937 } from '@tgslots/math/rng'
import type { Rng } from '@tgslots/math/rng/types'
import { Wager } from '@tgslots/slots-core/betting'
import { BET_CONFIG, BUY_BONUS_COST_MULTIPLIER, WoodlandWhisperSimulationStateMachine } from '@tgslots/woodland-whisper'
import { INNER_WEIGHTS, PICK_BONUS_TABLE, STRIP_STRINGS, Symbols } from '@tgslots/woodland-whisper/constants'
import { WoodlandWhisperModule } from '@tgslots-api/modules/woodland-whisper.module'
import { Mean, outcomeAt, recording, writeClientGolden, writeGolden, type Call, type ServedRound } from './shared'

const GOLDEN_ROUNDS = 60
const GOLDEN_SEED = 20260511
const SIMULATION_SEED = 7
const SIMULATED_ROUNDS = 60_000_000
const SIMULATED_BOUGHT_ROUNDS = 1_000_000
const CLIENT_SEED = 20260512
const ROWS = 3

const sizes = STRIP_STRINGS.map((strip) => strip.length)
const mysteryWeights = INNER_WEIGHTS.map(([, weight]) => weight)
const cardAmounts = PICK_BONUS_TABLE.map(([amount]) => amount)
const cardWeights = PICK_BONUS_TABLE.map(([, weight]) => weight)
const cardTotal = cardWeights.reduce((sum, weight) => sum + weight, 0)
const coins = (reel: number, stop: number) =>
  Array.from({ length: ROWS }, (_, row) => STRIP_STRINGS[reel]![(stop + row) % sizes[reel]!]).filter((name) => name === 'COIN').length
/** `stops[reel][count]`: the stops of a reel that show `count` COINs, as the graph's bought spin groups them. */
const stops = STRIP_STRINGS.map((strip, reel) => {
  const byCount: number[][] = Array.from({ length: ROWS + 1 }, () => [])
  strip.forEach((_, stop) => byCount[coins(reel, stop)]!.push(stop))
  return byCount
})
/** Every way to spread three or more COINs over the reels, in the order the graph lists them. */
const patterns: number[][] = []
const spread = (counts: number[]): void => {
  if (counts.length < stops.length) stops[counts.length]!.forEach((group, count) => group.length && spread([...counts, count]))
  else if (counts.reduce((sum, count) => sum + count, 0) >= 3) patterns.push(counts)
}
spread([])

/** `award` is the number of free spins the pick bonus gave after this spin, 0 when it was not played. */
interface Spin { board: number[]; win: number; scatters: number; award: number }
/**
 * `draws` are the graph's draw choices in its order: for every spin the mystery symbol and the five reel stops
 * (a bought spin: the mystery symbol, the COIN pattern, and each reel's stop among those showing its share),
 * then, where the pick bonus is played, the kind of every card turned up to the first pair.
 */
interface Round { draws: number[]; spins: Spin[]; win: number }

/** The cards the original turned to settle the award: its numbers out of the card weights, up to the first pair. */
function cardsTurned(calls: Call[], award: number): number[] {
  const first = calls.findIndex(([lo, hi]) => lo === 0 && hi === cardTotal)
  const turned: number[] = []
  for (let at = first; at >= 0 && at < calls.length; at++) {
    const kind = outcomeAt(cardWeights, calls[at]![2])
    turned.push(kind)
    if (turned.indexOf(kind) < turned.length - 1) {
      if (cardAmounts[kind] !== award) break
      return turned
    }
  }
  throw new Error(`The cards turned do not give the award of ${award}`)
}

/** What the original returns for one spin, as far as the draws of the graph depend on it. */
interface Shown { type: string; grid: number[][]; sc: number; pickedBonus: number }

/** The graph's draws for one spin of the original, from what the spin showed and the numbers it asked for. */
function drawsOf(result: Shown, calls: Call[]): number[] {
  // The original shows rows of columns; the graph's board is reel by reel.
  const column = (reel: number) => result.grid.map((row) => row[reel]!)
  const draws = [outcomeAt(mysteryWeights, calls[0]![2])]
  if (result.type === 'BUY') {
    // A bought spin does not ask for its stops one number each; read them off the board instead.
    const shown = sizes.map((_, reel) => column(reel).filter((symbol) => symbol === Symbols.COIN).length)
    const mystery = INNER_WEIGHTS[draws[0]!]![0]
    draws.push(patterns.findIndex((pattern) => pattern.every((count, reel) => count === shown[reel])))
    sizes.forEach((size, reel) => {
      const resolved = (stop: number, row: number) => {
        const symbol = Symbols[STRIP_STRINGS[reel]![(stop + row) % size] as keyof typeof Symbols]!
        return symbol === Symbols.REPLACEMENT ? mystery : symbol
      }
      const at = stops[reel]![shown[reel]!]!.findIndex((stop) => column(reel).every((symbol, row) => resolved(stop, row) === symbol))
      if (at < 0) throw new Error('A bought spin shows a window no stop gives')
      draws.push(at)
    })
  } else {
    const reelStops = calls.slice(1, 6)
    reelStops.forEach(([lo, hi], reel) => {
      if (lo !== 0 || hi !== sizes[reel]) throw new Error('A spin did not ask for its five reel stops after the mystery symbol')
    })
    draws.push(...reelStops.map(([, , stop]) => stop))
  }
  if (result.sc >= 3) draws.push(...cardsTurned(calls, result.pickedBonus))
  return draws
}

function play(base: Rng, wager: Wager, buy: boolean): Round {
  const { rng, take } = recording(base)
  const machine = new WoodlandWhisperSimulationStateMachine()
  const draws: number[] = []
  const spins: Spin[] = []
  let win = 0
  for (let result = buy ? machine.buyBonus(rng, wager) : machine.spin(rng, wager) as ReturnType<typeof machine.next>; result; result = machine.next(rng)) {
    const calls = take()
    if (result.type === 'PICK') continue
    draws.push(...drawsOf(result, calls))
    spins.push({ board: sizes.flatMap((_, reel) => result.grid.map((row) => row[reel]!)), win: result.win, scatters: result.sc, award: result.pickedBonus })
    win += result.win
  }
  return { draws, spins, win }
}

/**
 * One round as the original game server serves it to the client: a paid or bought spin, a request for every card
 * of a pick bonus, and a request per free spin. The player touches a different card every time.
 */
function serve(base: Rng, multiplier: number, buy: boolean): ServedRound {
  const { rng, take } = recording(base)
  const server = new WoodlandWhisperModule()
  let state = server.defaultState(rng)
  take()
  const round: ServedRound = { graph: buy ? 'woodland-whisper.buy.json' : 'woodland-whisper.json', multiplier, draws: [], exchanges: [] }
  const request = (action: 'spin' | 'buybonus' | 'freespin' | 'pick', payload: { multiplier?: number; userIndex?: number }) => {
    const answer = server.execute(rng, state, action, payload)
    state = answer.state
    const result = answer.result!
    if (result.type !== 'PICK') round.draws.push(...drawsOf(result, take()))
    round.exchanges.push({ action, payload, result: structuredClone(result), state: structuredClone(state) })
  }
  request(buy ? 'buybonus' : 'spin', { multiplier })
  for (let touched = 0; state.pickBonus || (state.freeSpins && state.freeSpins.spinsRemaining > 0); ) {
    if (state.pickBonus) request('pick', { userIndex: touched++ % state.pickBonus.board.length })
    else request('freespin', {})
  }
  return round
}

function reference(wager: Wager, buy: boolean, simulated: number) {
  // The first rounds as they come, then only rounds with a retrigger, so that the feature is covered in depth.
  const rounds: Round[] = []
  for (let rng = mt19937(GOLDEN_SEED); rounds.length < GOLDEN_ROUNDS; ) {
    const round = play(rng, wager, buy)
    if (rounds.length < GOLDEN_ROUNDS / 2 || round.spins.filter((spin) => spin.award > 0).length > (buy ? 1 : 0)) rounds.push(round)
  }

  const cost = wager.totalWager * (buy ? BUY_BONUS_COST_MULTIPLIER : 1)
  const rtp = new Mean(), firstSpin = new Mean(), hit = new Mean(), trigger = new Mean(), award = new Mean(), freeSpins = new Mean()
  for (let rng = mt19937(SIMULATION_SEED), i = 0; i < simulated; i++) {
    const round = play(rng, wager, buy)
    rtp.add(round.win / cost)
    firstSpin.add(round.spins[0]!.win / cost)
    hit.add(round.win > 0 ? 1 : 0)
    trigger.add(round.spins.length > 1 ? 1 : 0)
    if (round.spins.length > 1) freeSpins.add(round.spins.length - 1)
    for (const spin of round.spins) if (spin.award > 0) award.add(spin.award)
  }
  const simulation = {
    seed: SIMULATION_SEED, rounds: simulated, rtp: rtp.value, stdErr: rtp.stdErr, firstSpinRtp: firstSpin.value,
    hitFrequency: hit.value, triggerFrequency: trigger.value, award: award.value, awardStdErr: award.stdErr,
    freeSpinsPerTrigger: freeSpins.value,
  }
  return {
    source: `tgslots WoodlandWhisperSimulationStateMachine${buy ? ', bought feature' : ''}; rounds from mt19937(${GOLDEN_SEED}), simulation from mt19937(${SIMULATION_SEED})`,
    totalBet: wager.totalWager, roundCost: cost, simulation, rounds,
  }
}

/** Arguments `base` or `buy` regenerate only that file; without one, both. `client` writes the server's answers. */
export default function main({ golden, args }: { golden: string; args: string[] }) {
  if (args[0] === 'client') {
    // Four paid rounds as they come, four that earn a pick bonus and four bought, at bet multipliers 1, 2 and 3.
    const rounds: ServedRound[] = []
    for (let rng = mt19937(CLIENT_SEED); rounds.length < 12; ) {
      const round = serve(rng, 1 + (rounds.length % 3), rounds.length >= 8)
      if (rounds.length < 4 || round.exchanges.length > 1) rounds.push(round)
    }
    writeClientGolden(golden, 'woodland-whisper.client.golden.json', `tgslots WoodlandWhisperModule; rounds from mt19937(${CLIENT_SEED})`, rounds)
    return
  }
  const wager = new Wager(1, BET_CONFIG)
  if (args[0] !== 'buy') writeGolden(golden, 'woodland-whisper.golden.json', reference(wager, false, SIMULATED_ROUNDS))
  if (args[0] !== 'base') writeGolden(golden, 'woodland-whisper.buy.golden.json', reference(wager, true, SIMULATED_BOUGHT_ROUNDS))
}
