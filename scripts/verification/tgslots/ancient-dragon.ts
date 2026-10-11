import fs from 'node:fs'
import path from 'node:path'
import { mt19937 } from '@tgslots/math/rng'
import type { Rng } from '@tgslots/math/rng/types'
import { Wager } from '@tgslots/slots-core/betting'
import { AncientDragonStateMachine, BET_CONFIG } from '@tgslots/ancient-dragon'
import { INNER_WEIGHTS, STRIP_STRINGS } from '@tgslots/ancient-dragon/constants'
import { AncientDragonModule } from '@tgslots-api/modules/ancient-dragon.module'
import { recording, writeClientGolden, type ServedRound } from './shared'

const GOLDEN_ROUNDS = 60
const GOLDEN_SEED = 20260511
const SIMULATED_ROUNDS = 10_000_000
const SIMULATION_SEED = 7
const CLIENT_ROUNDS = 12
const CLIENT_SEED = 20260512

const sizes = STRIP_STRINGS.map((strip) => strip.length)
const mystery = INNER_WEIGHTS.map(([symbol]) => symbol)

interface Spin { board: number[]; win: number; scatters: number }
/** `draws` are the graph's draw choices in its order: for every spin the mystery symbol, then the five reel stops. */
interface Round { draws: number[]; spins: Spin[]; win: number }

/** The graph's draws for one spin of the original: the mystery symbol, then the five reel stops. */
function drawsOf(calls: number[][], grid: number[][]): number[] {
  // The reel stops are the last five numbers a spin asks for; the mystery symbol is sampled before them.
  const stops = calls.slice(-5)
  stops.forEach(([lo, hi], reel) => {
    if (lo !== 0 || hi !== sizes[reel]) throw new Error('A spin did not end with its five reel stops')
  })
  // The mystery symbol shows wherever a strip holds INNER; with no INNER in view its value changes nothing.
  let shown = mystery[0]!
  stops.forEach(([, , stop], reel) => {
    for (let row = 0; row < 3; row++) {
      if (STRIP_STRINGS[reel]![(stop! + row) % sizes[reel]!] === 'INNER') shown = grid[reel]![row]!
    }
  })
  return [mystery.indexOf(shown), ...stops.map(([, , stop]) => stop!)]
}

function play(base: Rng, wager: Wager): Round {
  let calls: number[][] = []
  const rng: Rng = (lo, hi) => {
    const value = base(lo, hi)
    calls.push([lo, hi, value])
    return value
  }
  const machine = new AncientDragonStateMachine()
  const draws: number[] = []
  const spins: Spin[] = []
  let win = 0
  for (let result = machine.spin(rng, wager) as ReturnType<typeof machine.next>; result; result = machine.next(rng)) {
    draws.push(...drawsOf(calls, result.grid))
    spins.push({ board: result.grid.flat(), win: result.win, scatters: result.sc })
    win += result.win
    calls = []
  }
  return { draws, spins, win }
}

/** One round as the original game server serves it to the client: a spin, then a request per free spin. */
function serve(base: Rng, multiplier: number): ServedRound {
  const { rng, take } = recording(base)
  const server = new AncientDragonModule()
  let state = server.defaultState(rng)
  const round: ServedRound = { graph: 'ancient-dragon.json', multiplier, draws: [], exchanges: [] }
  const request = (action: 'spin' | 'freespin', payload: { multiplier?: number }) => {
    take()
    const answer = server.execute(rng, state, action, payload)
    state = answer.state
    round.draws.push(...drawsOf(take().map((call) => [...call]), answer.result!.grid))
    round.exchanges.push({ action, payload, result: answer.result, state: structuredClone(state) })
  }
  request('spin', { multiplier })
  while (state.freeSpins && state.freeSpins.spinsRemaining > 0) request('freespin', {})
  return round
}

export default function main({ golden, args }: { golden: string; args: string[] }) {
  if (args[0] === 'client') {
    // Half the rounds as they come, half with free spins, at bet multipliers 1, 2 and 3 in turn.
    const rounds: ServedRound[] = []
    for (let rng = mt19937(CLIENT_SEED); rounds.length < CLIENT_ROUNDS; ) {
      const round = serve(rng, 1 + (rounds.length % 3))
      if (rounds.length < CLIENT_ROUNDS / 2 || round.exchanges.length > 1) rounds.push(round)
    }
    writeClientGolden(golden, 'ancient-dragon.client.golden.json', `tgslots AncientDragonModule; rounds from mt19937(${CLIENT_SEED})`, rounds)
    return
  }
  const wager = new Wager(1, BET_CONFIG)

  // The first rounds as they come, then only rounds with free spins, so the feature and its retriggers are covered.
  const rounds: Round[] = []
  for (let rng = mt19937(GOLDEN_SEED); rounds.length < GOLDEN_ROUNDS; ) {
    const round = play(rng, wager)
    if (rounds.length < GOLDEN_ROUNDS / 2 || round.spins.length > 1) rounds.push(round)
  }

  let total = 0, squares = 0, baseTotal = 0, hits = 0, triggers = 0, freeSpins = 0
  for (let rng = mt19937(SIMULATION_SEED), i = 0; i < SIMULATED_ROUNDS; i++) {
    const round = play(rng, wager)
    const win = round.win / wager.totalWager
    total += win
    squares += win * win
    baseTotal += round.spins[0]!.win / wager.totalWager
    if (round.win > 0) hits++
    if (round.spins.length > 1) {
      triggers++
      freeSpins += round.spins.length - 1
    }
  }
  const rtp = total / SIMULATED_ROUNDS
  const simulation = {
    seed: SIMULATION_SEED, rounds: SIMULATED_ROUNDS, rtp,
    stdErr: Math.sqrt((squares / SIMULATED_ROUNDS - rtp * rtp) / SIMULATED_ROUNDS),
    baseRtp: baseTotal / SIMULATED_ROUNDS, hitFrequency: hits / SIMULATED_ROUNDS,
    triggerFrequency: triggers / SIMULATED_ROUNDS, freeSpinsPerTrigger: freeSpins / triggers,
  }

  const file = path.join(golden, 'ancient-dragon.golden.json')
  fs.writeFileSync(file, JSON.stringify({
    source: `tgslots AncientDragonStateMachine; rounds from mt19937(${GOLDEN_SEED}), simulation from mt19937(${SIMULATION_SEED})`,
    totalBet: wager.totalWager, simulation, rounds,
  }) + '\n')
  console.log(`${file}: ${rounds.length} rounds, longest ${Math.max(...rounds.map((round) => round.spins.length))} spins`, simulation)
}
