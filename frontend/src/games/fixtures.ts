import { ancientDragonModel, createAncientDragonGraph } from './ancient-dragon/graph';
import { createLeMilitareGraph, leMilitareFeatures, leMilitareModel, type LeMilitareFeature } from './le-militare/graph';
import { createNineLivesGraph, nineLivesModel } from './nine-lives-tgslots/graph';
import { createWoodlandWhisperGraph, woodlandWhisperModel } from './woodland-whisper/graph';
import { createX7ClubGraph, x7ClubModel } from './x7-club/graph';

/**
 * Ancient Dragon cut down to five lines, tiny reels and at most two free spins. It has few enough outcomes for
 * the interpreters that visit every one, and it still has wilds, the mystery symbol, scatter pays, the trigger
 * and a retrigger.
 */
const smallAncientDragon = () => createAncientDragonGraph({
  ...ancientDragonModel,
  reel_strips: {
    reel1: ['YINYANG', 'GREENDRAGON', 'INNER', 'ACE'],
    reel2: ['YINYANG', 'GOLDDRAGON'],
    reel3: ['GREENDRAGON', 'KING'],
    reel4: ['GOLDDRAGON'],
    reel5: ['NINE'],
  },
  // A rare mystery symbol, so that pruning by probability has a branch to drop.
  inner_reel_strip: { reel1: ['GREENDRAGON', ...Array.from({ length: 9 }, () => 'ACE')] },
  paylines: ancientDragonModel.paylines.slice(0, 5),
  limits: { free_spin_cap: 2 },
}, 'ancient-dragon-small');

/**
 * Woodland Whisper cut down the same way: five lines, tiny reels, a pick bonus of two kinds of cards and at most
 * two free spins. It still has the wild, the mystery symbol, scatter pays, the pick bonus, doubled free spins and
 * a retrigger, and the bought feature has more than one way to show its scatters.
 */
const smallWoodlandWhisper = (buy: boolean) => createWoodlandWhisperGraph({
  ...woodlandWhisperModel,
  reel_strips: {
    reel1: ['COIN', 'CHEST', 'REPLACEMENT', 'A'],
    reel2: ['COIN', 'WOMAN'],
    reel3: ['A', 'TIARA'],
    reel4: ['TIARA'],
    reel5: ['A'],
  },
  // A rare mystery symbol, so that pruning by probability has a branch to drop.
  inner_reel_strip: { reel1: ['TIARA', ...Array.from({ length: 9 }, () => 'A')] },
  feature: { ...woodlandWhisperModel.feature, pick_bonus: [[2, 1], [1, 3]] },
  paylines: woodlandWhisperModel.paylines.slice(0, 5),
  limits: { free_spin_cap: 2 },
}, { buy, graphId: buy ? 'woodland-whisper-buy-small' : 'woodland-whisper-small' });

/**
 * X7 Club cut down to one row of three cells and one payline, three symbols, two prizes, two respins and a boost
 * of at most two pulls. Every coin is then a full column, so every coin is boosted, and the round cap is low
 * enough to cut real wins. It still has the wild, coins with prizes, the Hold & Spin trigger and every kind of pull
 * that matters: one that ends the boost, one that adds, one that multiplies.
 */
const smallX7Club = (buy: boolean) => createX7ClubGraph({
  ...x7ClubModel,
  game_metadata: { ...x7ClubModel.game_metadata, cost_to_play: 1 },
  // A rare wild, so that pruning by probability has a branch to drop.
  cell_weights: { CHILL: 5, WILD: 1, COIN: 4 },
  paytable: { CHILL: { 3: 2 } },
  prizes: [{ tier: 'CREDIT', bets: 1, weight: 3 }, { tier: 'MINI', bets: 4, weight: 1 }],
  hold: { rows: 1, trigger: 2, respins: 2, hit_weight: 1, miss_weight: 3 },
  column_boost: { max_pulls: 2, kinds: x7ClubModel.column_boost.kinds.filter(({ kind }) => kind !== 'PLUS2') },
  buy: { cost_multiplier: 4, entry_credits: 3, cells: [0, 2] },
  max_win: 20,
  paylines: [[0, 0, 0]],
}, { buy, graphId: buy ? 'x7-club-buy-small' : 'x7-club-small' });

/**
 * Nine Lives cut down to a board of two columns and two rows, two paying symbols, groups of three or four cells
 * and a multiplier of at most 3. Any three cells of this board touch, so it still has groups joined by a wild,
 * symbols falling and dropping in, the scatter trigger, collected coins that leave wilds, and a round limit low
 * enough to cut real wins. The paid game has one free spin and cascades of two steps, the second paying double;
 * the bought bonus has two free spins of one step each, so that the multiplier is carried from one to the next.
 */
const smallNineLives = (buy: boolean) => createNineLivesGraph({
  ...nineLivesModel,
  game_metadata: { ...nineLivesModel.game_metadata, cost_to_play: 2 },
  board: { columns: 2, rows: 2 },
  cell_weights: {
    // A rare wild and a rare coin, so that pruning by probability has a branch to drop in either kind of spin.
    paid_spin: { WILD: 1, BONE: 4, FISH: 3, SCATTER: 2 },
    free_spin: { BONE: 10, FISH: 9, COIN: 1 },
    // A bought cascade ends after its one step, so what drops in is never paid: one symbol keeps the outcomes few.
    refill: buy ? { FISH: 1 } : { BONE: 2, FISH: 1 },
  },
  cluster_paytable: { BONE: { 3: 1, 4: 4 }, FISH: { 3: 2, 4: 6 } },
  prizes: [{ bets: 1, weight: 1 }],
  free_spins: { trigger: 3, spins: buy ? 2 : 1, max_multiplier: 3 },
  cascade: { max_steps: buy ? 1 : 2 },
  buy: { cost_multiplier: 3 },
  max_win: 8,
}, { buy, graphId: buy ? 'nine-lives-buy-small' : 'nine-lives-small' });

/**
 * Le Militare cut down to a board of two reels and two rows, strips of two or three symbols, one paying symbol,
 * groups of three or four cells, two multipliers and cascades of two steps. It still has a reel that arms on a
 * launcher and turns wild, planes shot down for a multiplier, the air raid, held cells, the armed reel that
 * counts once in the free spins, scatters that start the free spins and award more, and a round limit low enough
 * to cut real wins.
 */
const smallLeMilitare = (feature: LeMilitareFeature) => createLeMilitareGraph({
  ...leMilitareModel,
  board: { columns: 2, rows: 2 },
  cluster_paytable: { BULLET: { 3: 100, 4: 300 } },
  reel_strips: {
    // The launcher is on the first reel, which is the reel the largest bought tier arms from the start, and the
    // planes are on the second, so that an armed reel always has planes to shoot down.
    paid_spin: [['BULLET', 'BULLET', 'S300'], ['BULLET', 'PLANE', 'SCATTER']],
    free_spin: [['BULLET', 'BULLET', 'S300'], ['BULLET', 'PLANE', 'SCATTER']],
  },
  // Free spins bought outright open with a spin that cannot pay, which leaves room for two free spins; a round
  // that opens with a spin of its own has one.
  free_spins: { trigger: 2, awards: { 2: 1, 3: 2 }, limit: feature === 'standard' || feature === 'elite' || feature === 'super' ? 2 : 1 },
  cascade: { max_steps: 2 },
  modes: { assault: {
    // A rare multiplier, so that pruning by probability has a branch to drop.
    multipliers: [{ times: 1, weight: 9 }, { times: 4, weight: 1 }],
    air_raid: { fire_weight: 1, quiet_weight: 3, squadrons: [{ planes: 1, weight: 1 }], hit_weight: 1, miss_weight: 1 },
    prices: { standard: 150, elite: 250, super: 400, chance: 130, air_raid: 120 },
  } },
  buy: { standard: { scatters: 2, armed_reels: 0, multiplier: 0 }, elite: { scatters: 3, armed_reels: 0, multiplier: 0 },
    super: { scatters: 3, armed_reels: 1, multiplier: 2 }, chance: { force_weight: 1, pass_weight: 4, scatters: 2 } },
  max_win: 8,
}, { feature, graphId: `le-militare-${feature}-small` });

/**
 * The game graphs exported for the backend test suites, by file name. The backend never imports
 * frontend code: `scripts/export-game-fixtures.mjs` writes these as JSON and CI checks they are current.
 */
export const gameFixtures: Record<string, () => Record<string, unknown>> = {
  'ancient-dragon.json': () => createAncientDragonGraph(),
  'ancient-dragon.small.json': smallAncientDragon,
  'woodland-whisper.json': () => createWoodlandWhisperGraph(),
  'woodland-whisper.buy.json': () => createWoodlandWhisperGraph(undefined, { buy: true }),
  'woodland-whisper.small.json': () => smallWoodlandWhisper(false),
  'woodland-whisper.buy.small.json': () => smallWoodlandWhisper(true),
  // Le Militare has three modes and six kinds of round. Every kind of round is exported in the default mode, and
  // the paid spin in the other two, whose graphs differ from the default mode's only in the tables of the mode.
  ...Object.fromEntries(leMilitareFeatures.map(feature => [`le-militare.${feature}.json`, () => createLeMilitareGraph(undefined, { feature })])),
  ...Object.fromEntries(Object.keys(leMilitareModel.modes).filter(mode => mode !== leMilitareModel.default_mode)
    .map(mode => [`le-militare.${mode}.spin.json`, () => createLeMilitareGraph(undefined, { mode })])),
  ...Object.fromEntries(leMilitareFeatures.map(feature => [`le-militare.${feature}.small.json`, () => smallLeMilitare(feature)])),
  // What every one of the eighteen rounds costs, in credits, as its graph states it.
  'le-militare.prices.json': () => Object.fromEntries(Object.keys(leMilitareModel.modes).map(mode => [mode, Object.fromEntries(
    leMilitareFeatures.map(feature => [feature, (createLeMilitareGraph(undefined, { mode, feature }).initialState as { roundCost: number }).roundCost]))])),
  'nine-lives.json': () => createNineLivesGraph(),
  'nine-lives.buy.json': () => createNineLivesGraph(undefined, { buy: true }),
  'nine-lives.small.json': () => smallNineLives(false),
  'nine-lives.buy.small.json': () => smallNineLives(true),
  'x7-club.json': () => createX7ClubGraph(),
  'x7-club.buy.json': () => createX7ClubGraph(undefined, { buy: true }),
  'x7-club.small.json': () => smallX7Club(false),
  'x7-club.buy.small.json': () => smallX7Club(true),
};
