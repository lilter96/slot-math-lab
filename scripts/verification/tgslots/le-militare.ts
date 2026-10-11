import fs from 'node:fs'
import path from 'node:path'
import { mt19937 } from '@tgslots/math/rng'
import type { Rng } from '@tgslots/math/rng/types'
import { Wager } from '@tgslots/slots-core/betting'
import {
  BET_CONFIG, BUY_OPTIONS, CHANCE_SPIN_FORCE_WEIGHTS, DEFAULT_MODE, INT_STRIPS_BASE, INT_STRIPS_FREE, MAX_WIN_MULTIPLIER, MODE_CONFIGS, MODE_IDS,
  REEL_COUNT, ROW_COUNT, SYM_NAMES, getFeatureBuyCost, type ModeId,
} from '@tgslots/le-militare/constants'
import { LeMilitareStateMachine, type LeMilitareResult } from '@tgslots/le-militare/game-state-machine'
import { LeMilitareModule } from '@tgslots-api/modules/le-militare.module'
import { Mean, outcomeAt, recording, writeClientGolden, writeGolden, type Call, type ServedRound } from './shared'

const GOLDEN_SEED = 20260511
const SIMULATION_SEED = 7
const CLIENT_SEED = 20260512
const CELLS = REEL_COUNT * ROW_COUNT

type Feature = 'spin' | 'chance' | 'air-raid' | 'standard' | 'elite' | 'super'
const features: readonly Feature[] = ['spin', 'chance', 'air-raid', 'standard', 'elite', 'super']
const isTier = (feature: Feature): feature is 'standard' | 'elite' | 'super' => feature in BUY_OPTIONS
/** What the round costs, in bets, under the name the original prices it by. */
const price = (mode: ModeId, feature: Feature) => feature === 'spin' ? 1 : getFeatureBuyCost(mode, feature === 'air-raid' ? 'airraid' : feature)
/**
 * How many rounds of the original are simulated for each kind of round. Free spins bought outright are a hundred
 * times slower to play than a spin, and their price makes their return per credit staked that much steadier.
 */
const simulated: Record<Feature, number> = { spin: 40_000_000, chance: 20_000_000, 'air-raid': 20_000_000, standard: 1_000_000, elite: 400_000, super: 300_000 }
/** How many rounds are kept for the replay: half as they come, half chosen for what happens in them. */
const kept: Record<Feature, number> = { spin: 40, chance: 24, 'air-raid': 24, standard: 8, elite: 6, super: 6 }

const total = (weights: readonly number[]) => weights.reduce((sum, weight) => sum + weight, 0)
/** Board order of the graph: reel by reel, top to bottom. The original keeps its snapshots row by row. */
const cells = (grid: number[][]) => Array.from({ length: CELLS }, (_, cell) => grid[cell % ROW_COUNT]![Math.floor(cell / ROW_COUNT)]!)
const ascending = (values: readonly number[]) => [...values].sort((a, b) => a - b)

/** A group that paid: its symbol, its size as the paytable counts it, its pay in bets, and its cells in board order. */
interface Hit { symbol: number; size: number; pay: number; cells: number[] }
/**
 * One board the game looked at. `before` and `after` are the board around the combat, `shots` the multipliers the
 * planes left in board order, `armed` the reels that armed. A board that paid has `hits`, the cells `removed`
 * from it, and the cells `held` once the symbols have fallen.
 */
interface Look { spin: number; before: number[]; after: number[]; shots: number[]; armed: number[]; win: number; hits: Hit[]; removed: number[]; held: number[] }
/** A spin: the board it landed with, and what it ended with — scatters, what its groups paid, its multiplier, its win, its armed reels. */
interface Spin { board: number[]; scatters: number; groups: number; multiplier: number; win: number; paid: number; armed: number[] }
/** The air raid of the opening spin: the board before it, the size of the squadron, and where its planes fell with what multiplier. */
interface Raid { board: number[]; squadron: number; cells: number[]; multipliers: number[] }
/** `draws` are the graph's draw choices in its order. */
interface Round { draws: number[]; spins: Spin[]; looks: Look[]; raid: Raid | null; win: number; freeSpins: number; capped: boolean }

/**
 * Reads the numbers one spin asked its generator for as the choices the graph makes at its draws. The original
 * asks for a number out of the total weight of what it draws, and three of its draws share a total, so they are
 * told apart by where they come in the spin: the chance roll first, the six stops, the air raid, and after that
 * only multipliers and the stops of reels that fill up.
 */
function choices(calls: readonly Call[], mode: ModeId, opening: Feature | null): number[] {
  const { multiplierWeights, airRaid } = MODE_CONFIGS[mode]
  const multipliers = multiplierWeights.map(([, weight]) => weight)
  const strips = (opening ? INT_STRIPS_BASE : INT_STRIPS_FREE).map((strip) => strip.length - 2)
  const out: number[] = []
  let next = 0
  const call = (range: number) => {
    const [lo, hi, value] = calls[next++] ?? [0, 0, 0]
    if (lo !== 0 || hi !== range) throw new Error(`The original asked for a number out of [${lo}, ${hi}) where one out of ${range} was expected`)
    return value
  }
  const uniform = (range: number) => { const value = call(range); out.push(value); return value }
  const weighted = (weights: readonly number[]) => {
    if (weights.some((weight) => weight <= 0)) throw new Error('A weight of the original is not positive')
    const index = outcomeAt(weights, call(total(weights)))
    out.push(index)
    return index
  }
  if (opening === 'chance') weighted(CHANCE_SPIN_FORCE_WEIGHTS)
  for (const length of strips) uniform(length)
  const raided = opening === 'air-raid' || ((opening === 'spin' || opening === 'chance') && weighted(airRaid.triggerWeights) === 0)
  if (raided)
    for (let plane = airRaid.squadronSizes[weighted(airRaid.squadronWeights)]!; plane > 0; plane--)
      if (weighted(airRaid.hitWeights) === 0) {
        uniform(REEL_COUNT)
        uniform(ROW_COUNT)
        weighted(multipliers)
      }
  while (next < calls.length) {
    const range = calls[next]![1]
    if (range === total(multipliers)) weighted(multipliers)
    else if (strips.includes(range)) uniform(range)
    else throw new Error(`The original asked for a number out of ${range} during a cascade`)
  }
  return out
}

function play(base: Rng, wager: Wager, mode: ModeId, feature: Feature, detailed: boolean): Round {
  const { rng, take } = recording(base)
  const source = detailed ? rng : base
  const machine = new LeMilitareStateMachine(undefined, mode)
  const round: Round = { draws: [], spins: [], looks: [], raid: null, win: 0, freeSpins: 0, capped: false }
  const first: LeMilitareResult = feature === 'spin' ? machine.spin(source, wager) : feature === 'chance' ? machine.buyChanceSpin(source, wager)
    : feature === 'air-raid' ? machine.buyAirRaidSpin(source, wager) : machine.buyBonus(source, wager, feature)
  for (let result: LeMilitareResult | null = first, index = 0; result; result = machine.next(source), index++) {
    const spin = machine.state.lastSpinResult!
    round.win += result.win
    if (index > 0) round.freeSpins++
    round.spins.push({ board: detailed ? cells(spin.initialGrid) : [], scatters: spin.scatterCount, groups: spin.baseClusterWin,
      multiplier: spin.endMultiplierSum, win: spin.finalWin, paid: result.win, armed: ascending(spin.endArmedReels) })
    if (!detailed) continue
    round.draws.push(...choices(take(), mode, index === 0 ? feature : null))
    if (spin.airRaid)
      round.raid = { board: cells(spin.airRaid.preRaidGrid), squadron: spin.airRaid.squadronSize,
        cells: spin.airRaid.placements.map(({ reel, row }) => reel * ROW_COUNT + row), multipliers: spin.airRaid.placements.map(({ multiplier }) => multiplier) }
    for (const step of spin.steps)
      round.looks.push({
        spin: index, before: cells(step.preCombatGrid), after: cells(step.postCombatGrid), shots: step.shootdowns.map(({ multiplier }) => multiplier),
        armed: step.activations.map(({ reel }) => reel), win: step.stepWin, removed: ascending(step.vanishedPositions), held: ascending(step.stickyWildPositions),
        hits: step.hits.map((hit) => ({ symbol: hit.symbolId, size: hit.size, pay: hit.basePayout, cells: ascending(hit.positions) })),
      })
  }
  round.capped = round.win >= MAX_WIN_MULTIPLIER * wager.totalWager
  return round
}

/** The request that starts each kind of round. Free spins are bought by their tier, and the first tier is the default. */
const starters: Record<Feature, { action: string; option?: string }> = {
  spin: { action: 'spin' }, chance: { action: 'chancespin' }, 'air-raid': { action: 'airraidspin' },
  standard: { action: 'buybonus' }, elite: { action: 'buybonus', option: 'elite' }, super: { action: 'buybonus', option: 'super' },
}

/** One round as the original game server serves it to its client: every request of the round and what it answered. */
function serve(base: Rng, multiplier: number, mode: ModeId, feature: Feature): ServedRound {
  const { rng, take } = recording(base)
  const module = new LeMilitareModule()
  let state = module.defaultState(rng)
  const round: ServedRound = { graph: `le-militare.${mode === DEFAULT_MODE ? '' : `${mode}.`}${feature}.json`, multiplier, draws: [], exchanges: [] }
  const request = (action: string, payload: Record<string, unknown>, opening: Feature | null) => {
    const refused = module.validateAction(state, action as never, payload)
    if (refused) throw new Error(refused)
    const answer = module.execute(rng, state, action as never, payload)
    state = answer.state
    round.draws.push(...choices(take(), mode, opening))
    round.exchanges.push({ action, payload, result: structuredClone(answer.result), state: structuredClone(state) })
  }
  const { action, option } = starters[feature]
  request(action, { multiplier, ...(option ? { option } : {}), ...(mode === DEFAULT_MODE ? {} : { mode }) }, feature)
  while (state.freeSpins && state.freeSpins.spinsRemaining > 0) request('freespin', {}, null)
  return round
}

/** The rounds the ported client is checked against: every kind of round, the other modes, and what a spin can bring. */
const served: { feature: Feature; mode?: ModeId; when?: (results: LeMilitareResult[]) => boolean }[] = [
  { feature: 'spin' }, { feature: 'spin' }, { feature: 'spin' },
  { feature: 'spin', when: (results) => results.length > 1 },
  { feature: 'spin', when: ([first]) => first!.type === 'BASE' && (first.airRaid?.placements.length ?? 0) > 0 },
  { feature: 'spin', when: (results) => results.some((result) => result.steps.some((step) => step.shootdowns.length > 0 && step.activations.length > 0)) },
  { feature: 'spin', when: ([first]) => first!.steps.filter((step) => step.hits.length > 0).length >= 3 },
  { feature: 'chance' }, { feature: 'chance', when: (results) => results.length > 1 },
  { feature: 'air-raid' }, { feature: 'air-raid', when: ([first]) => first!.win > 0 },
  { feature: 'standard' }, { feature: 'elite' }, { feature: 'super' },
  { feature: 'spin', mode: 'recon', when: ([first]) => first!.win > 0 }, { feature: 'spin', mode: 'siege', when: ([first]) => first!.win > 0 },
]

/** What makes a round worth keeping for the replay; the kept rounds take these in turn. */
const chosen: ((round: Round) => boolean)[] = [
  (round) => round.freeSpins > 0,
  (round) => (round.raid?.cells.length ?? 0) > 0,
  (round) => round.looks.some((look) => look.shots.length > 0 && look.armed.length > 0),
  (round) => round.looks.filter((look) => look.spin === 0 && look.hits.length > 0).length >= 3,
]

function reference(mode: ModeId, feature: Feature, rounds: number) {
  const wager = new Wager(1, BET_CONFIG)
  const golden: Round[] = []
  for (let rng = mt19937(GOLDEN_SEED); golden.length < kept[feature]; ) {
    const round = play(rng, wager, mode, feature, true)
    const picked = golden.length - kept[feature] / 2
    if (picked < 0 || isTier(feature) || chosen[picked % chosen.length]!(round)) golden.push(round)
  }

  const cost = price(mode, feature)
  const rtp = new Mean(), opening = new Mean(), hit = new Mean(), bonus = new Mean(), scatters = new Mean(), openingMultiplier = new Mean(),
    freeSpins = new Mean(), finalMultiplier = new Mean(), capped = new Mean()
  let mostFreeSpins = 0
  for (let rng = mt19937(SIMULATION_SEED), i = 0; i < rounds; i++) {
    const round = play(rng, wager, mode, feature, false)
    rtp.add(round.win / cost)
    opening.add(round.spins[0]!.paid)
    hit.add(round.win > 0 ? 1 : 0)
    bonus.add(round.freeSpins > 0 ? 1 : 0)
    scatters.add(round.spins[0]!.scatters)
    openingMultiplier.add(round.spins[0]!.multiplier)
    freeSpins.add(round.freeSpins)
    finalMultiplier.add(round.spins[round.spins.length - 1]!.multiplier)
    capped.add(round.capped ? 1 : 0)
    mostFreeSpins = Math.max(mostFreeSpins, round.freeSpins)
  }
  const simulation = {
    seed: SIMULATION_SEED, rounds, rtp: rtp.value, stdErr: rtp.stdErr, openingWin: opening.value, openingWinStdErr: opening.stdErr,
    hitFrequency: hit.value, bonusFrequency: bonus.value, openingScatters: scatters.value, openingScattersStdErr: scatters.stdErr,
    openingMultiplier: openingMultiplier.value, openingMultiplierStdErr: openingMultiplier.stdErr, freeSpins: freeSpins.value, freeSpinsStdErr: freeSpins.stdErr,
    finalMultiplier: finalMultiplier.value, finalMultiplierStdErr: finalMultiplier.stdErr, cappedFrequency: capped.value, mostFreeSpins,
  }
  return {
    source: `tgslots LeMilitareStateMachine, ${mode} mode, ${feature}; rounds from mt19937(${GOLDEN_SEED}), simulation from mt19937(${SIMULATION_SEED})`,
    mode, feature, price: cost, simulation, rounds: golden,
  }
}

/**
 * Without arguments every reference file is regenerated. `spin`, `chance`, … regenerate that kind of round in the
 * default mode, `recon.spin` the paid spin of another mode, and `tables` only the tables of the original. A
 * number after the name overrides how many rounds are simulated. `client` writes the rounds the ported client is
 * checked against instead.
 */
export default function main({ golden, args }: { golden: string; args: string[] }) {
  if (args[0] === 'client') {
    const rounds: ServedRound[] = []
    for (let rng = mt19937(CLIENT_SEED); rounds.length < served.length; ) {
      const { feature, mode = DEFAULT_MODE, when = () => true } = served[rounds.length]!
      const round = serve(rng, 1 + (rounds.length % 3), mode, feature)
      if (when(round.exchanges.map((exchange) => exchange.result as LeMilitareResult))) rounds.push(round)
    }
    writeClientGolden(golden, 'le-militare.client.golden.json', `tgslots LeMilitareModule; rounds from mt19937(${CLIENT_SEED})`, rounds)
    return
  }
  const variants = [
    ...features.map((feature) => ({ name: feature as string, mode: DEFAULT_MODE, feature })),
    ...MODE_IDS.filter((mode) => mode !== DEFAULT_MODE).map((mode) => ({ name: `${mode}.spin`, mode, feature: 'spin' as Feature })),
  ]
  if (!args[0] || args[0] === 'tables') {
    // The tables of the original as it ships them, for the graphs' own tables to be held against.
    const config = JSON.parse(fs.readFileSync(path.join(process.env.TGSLOTS_DIR!, 'packages/games/le-militare/config/config.json'), 'utf8'))
    fs.writeFileSync(path.join(golden, 'le-militare.tables.golden.json'), JSON.stringify({
      source: 'tgslots packages/games/le-militare: config/config.json and the tables src/constants.ts builds from it',
      symbols: SYM_NAMES, config, maxWin: MAX_WIN_MULTIPLIER, defaultMode: DEFAULT_MODE,
      prices: Object.fromEntries(MODE_IDS.map((mode) => [mode, Object.fromEntries(features.map((feature) => [feature, price(mode, feature)]))])),
    }) + '\n')
  }
  for (const { name, mode, feature } of variants)
    if (!args[0] || args[0] === name)
      writeGolden(golden, `le-militare.${name}.golden.json`, reference(mode, feature, Number(args[1] ?? simulated[feature])))
}
