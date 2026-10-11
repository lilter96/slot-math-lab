import fullModel from './model.json' with { type: 'json' };
import { add, and, at, countOver, div, emptyArray, eq, except, floor, foldOver, gt, gte, iteration, length, lt, lte, mapOver, max, min, mul, neq, num, only, or, state, str, sub, sumOver, toNumber, when } from '../shared/graph/expr';
import { Flow, gameGraph, Mechanics } from '../shared/graph/flow';
import { cascade, cascadeState, tumble, type Carried } from '../shared/graph/mechanics/cascade';
import { clusterPays, clusterState, clusterTrace, type ClusterBoard } from '../shared/graph/mechanics/clusters';
import { reelState, spinReels, type ReelSet } from '../shared/graph/mechanics/reels';
import { gameTables } from '../shared/graph/tables';

/** The tables of one volatility mode: the multipliers a plane leaves, the air raid, and what the features cost in credits. */
interface ModeTables {
  multipliers: { times: number; weight: number }[];
  air_raid: { fire_weight: number; quiet_weight: number; squadrons: { planes: number; weight: number }[]; hit_weight: number; miss_weight: number };
  prices: { standard: number; elite: number; super: number; chance: number; air_raid: number };
}
export type LeMilitareModel = Omit<typeof fullModel, 'cluster_paytable' | 'reel_strips' | 'free_spins' | 'modes'> & {
  /** `cluster_paytable[symbol][size]`, in credits. */
  cluster_paytable: Record<string, Record<string, number>>;
  reel_strips: { paid_spin: string[][]; free_spin: string[][] };
  /** `awards[scatters]` free spins; a count between two entries is awarded as the lower one. */
  free_spins: { trigger: number; awards: Record<string, number>; limit: number };
  modes: Record<string, ModeTables>;
};
/** What a round is: a paid spin, a spin bought with a better chance or a sure air raid, or free spins bought outright. */
export type LeMilitareFeature = 'spin' | 'chance' | 'air-raid' | 'standard' | 'elite' | 'super';
export const leMilitareFeatures: readonly LeMilitareFeature[] = ['spin', 'chance', 'air-raid', 'standard', 'elite', 'super'];
export interface LeMilitareOptions {
  graphId?: string;
  /** The volatility mode, a key of `model.modes`; the default is the model's own. */
  mode?: string;
  feature?: LeMilitareFeature;
}

/** What a played round leaves in its trace about every spin, about every board looked at, and about every step that paid. */
const spinTrace = ['spinBoards', 'spinScatters', 'spinGroupCoins', 'spinMultipliers', 'spinWins', 'spinTotals', 'spinArmed'] as const;
const lookTrace = ['lookSpins', 'lookBefore', 'lookAfter', 'lookShots', 'lookArmed', 'lookHeld'] as const;
const stepTrace = ['stepSpins', 'stepWins', 'stepHeld'] as const;
const raidTrace = ['raidBoards', 'raidSquadrons', 'raidCells', 'raidMultipliers'] as const;
/** The groups that paid in a step, under the names the cluster subgraphs trace them by: `hitSymbols` is kept as `stepHitSymbols`. */
const stepHits = (key: string) => `stepH${key.slice(1)}`;

const numbered = { index: 'place', itemType: 'Number' } as const;
const zeros = (count: number) => Array.from({ length: count }, () => 0);

/**
 * Le Militare as a graph: reels that stop on strips, cluster pays and cascades. A reel that shows a launcher arms
 * and turns WILD; while a reel is armed every plane on the board is shot down, becomes a WILD and adds a
 * multiplier, and the spin pays its groups times the sum of its multipliers. Scatters start free spins, in which
 * armed reels and the multiplier are kept from spin to spin. Everything specific to the game is read from `model`;
 * the default is the released game in its default mode. `feature` selects what the round is, and a round that is
 * bought reports its payout per credit of its price.
 */
export function createLeMilitareGraph(model: LeMilitareModel = fullModel, options: LeMilitareOptions = {}): Record<string, unknown> {
  const { mode = model.default_mode, feature = 'spin' } = options;
  const graphId = options.graphId ?? ['le-militare', ...(mode === model.default_mode ? [] : [mode]), ...(feature === 'spin' ? [] : [feature])].join('-');
  const tables = model.modes[mode];
  if (!tables) throw new Error(`Le Militare has no mode '${mode}'`);

  const names: readonly string[] = model.symbols.regular;
  const id = (name: string) => names.indexOf(name);
  const WILD = id(model.symbols.wild), PLANE = id(model.symbols.plane), LAUNCHER = id(model.symbols.launcher), SCATTER = id(model.symbols.scatter);
  const { wild: wildName, plane: planeName, launcher: launcherName, scatter: scatterName } = model.symbols;
  const { columns: COLUMNS, rows: ROWS } = model.board, CELLS = COLUMNS * ROWS;
  const TOTAL_BET = model.game_metadata.cost_to_play;
  const tier = feature === 'standard' || feature === 'elite' || feature === 'super' ? model.buy[feature] : undefined;
  const ROUND_COST = feature === 'spin' ? TOTAL_BET : tables.prices[feature === 'air-raid' ? 'air_raid' : feature];
  // The most a round may pay, a rule of the game: a multiple of the bet, whatever the round cost. The graph
  // applies it in credits; the sink is told the same limit in the unit it reports, rounded up.
  const CAP_COINS = model.max_win * TOTAL_BET;
  const { trigger: TRIGGER, awards, limit: LIMIT } = model.free_spins;
  const MOST_SCATTERS = Math.max(...Object.keys(awards).map(Number));
  const awardFor = (scatters: number) => {
    for (let count = Math.min(scatters, MOST_SCATTERS); count >= TRIGGER; count--) if (awards[count] !== undefined) return awards[count];
    return 0;
  };
  const awarded = at('freeSpinAwards', min(state('scatterCount'), num(MOST_SCATTERS)));
  const awardText = Object.entries(awards).map(([scatters, spins]) => `${scatters} → ${spins}`).join(', ');
  // The scatters a bought round places: left to right along the top row, then along the next.
  const placed = feature === 'chance' ? model.buy.chance.scatters : tier?.scatters ?? 0;
  const placedScatters = zeros(CELLS);
  for (let index = 0; index < placed; index++) placedScatters[(index % COLUMNS) * ROWS + Math.floor(index / COLUMNS)] = 1;

  const mechanics = new Mechanics();
  const strips = (rows: string[][]) => rows.map(strip => strip.map(id));
  const paidReels: ReelSet = { name: 'paid-reels', stripKey: 'paidStrip', strips: strips(model.reel_strips.paid_spin), rows: ROWS };
  const freeReels: ReelSet = { name: 'free-reels', stripKey: 'freeStrip', strips: strips(model.reel_strips.free_spin), rows: ROWS };
  const clusters: ClusterBoard = { columns: COLUMNS, rows: ROWS, wild: WILD, symbolCount: names.length, weights: 'cellCounts',
    pays: Object.fromEntries(Object.entries(model.cluster_paytable).map(([name, row]) => [id(name), row])) };
  const held: Carried = { key: 'held', label: 'held cells', fresh: 0 };

  const place = state('place'), item = state('item');
  /** The reel of the cell `place`. */
  const reel = floor(div(place, num(ROWS)));
  const capped = gte(state('roundCoins'), state('capCoins'));
  // In the free spins an armed reel is one giant WILD: its top cell counts towards the size of a group, the rest do not.
  const giantCounts = mapOver('cellCounts',
    when(and(eq(state('bonus'), num(1)), eq(at('armed', reel), num(1)), neq(place, mul(reel, num(ROWS)))), num(0), num(1)), numbered);

  const multiplier = mechanics.add(new Flow()
    .draw('multiplier', 'Draw the multiplier', 'drawnMultiplier', tables.multipliers.map(({ times, weight }) => [times, weight] as const))
    .write('sum', 'Add it to the multiplier of the spin', 'multiplierSum', add(state('multiplierSum'), toNumber(state('drawnMultiplier'))))
    .record('trace', 'Remember it', 'drawnMultipliers', toNumber(state('drawnMultiplier')))
    .mechanic('add-multiplier', 'Draws a multiplier and adds it to the multiplier of the spin.'));

  const combat = mechanics.add(new Flow()
    .record('trace-spin', 'Remember which spin the board belongs to', 'lookSpins', sub(length('spinBoards'), num(1)))
    .record('trace-before', 'Remember the board before the combat', 'lookBefore', state('board'))
    .write('fresh', 'No plane has been shot down on this board yet', 'drawnMultipliers', emptyArray())
    .write('launchers', `A reel that shows a ${launcherName} and is not armed yet arms now`, 'newlyArmed',
      mapOver('armed', when(and(eq(item, num(0)),
        or(...Array.from({ length: ROWS }, (_, row) => eq(at('board', add(mul(place, num(ROWS)), num(row))), str(LAUNCHER))))), num(1), num(0)), numbered))
    .either('armed', 'Is any reel armed?',
      gt(add(sumOver('armed', item, { itemType: 'Number' }), sumOver('newlyArmed', item, { itemType: 'Number' })), num(0)), path => path
        .write('planes', `Count the ${planeName}s on the board`, 'planeCount', countOver('board', eq(item, str(PLANE))))
        // Never more planes than cells, and one pass of slack so that the limit is never what ends the loop.
        .repeat('shoot', `Every ${planeName} is shot down and leaves a multiplier`, CELLS + 1, multiplier,
          { until: gte(iteration('shoot'), state('planeCount')), bodyLabel: `One ${planeName}` })
        .either('changed', `Was a ${planeName} shot down, or did a reel arm?`,
          gt(add(state('planeCount'), sumOver('newlyArmed', item, { itemType: 'Number' })), num(0)), changed => changed
            .write('hold', `A ${planeName} shot down in a paid spin is held, and so is a reel that arms in the free spins`, 'held',
              mapOver('held', when(eq(at('newlyArmed', reel), num(1)), state('bonus'),
                when(eq(at('board', place), str(PLANE)), sub(num(1), state('bonus')), item)), numbered))
            .write('wilds', `Every ${planeName} shot down and every cell of a reel that arms becomes a ${wildName}`, 'board',
              mapOver('board', when(or(eq(at('newlyArmed', reel), num(1)), eq(item, str(PLANE))), str(WILD), item), { index: 'place' }))
            .write('arm', 'A reel stays armed once it has armed', 'armed', mapOver('armed', max(item, at('newlyArmed', place)), numbered))
            .write('giants', 'In the free spins an armed reel counts as one cell of a group', 'cellCounts', giantCounts)))
    .record('trace-after', 'Remember the board after the combat', 'lookAfter', state('board'))
    .record('trace-shots', 'Remember the multipliers the planes left', 'lookShots', state('drawnMultipliers'))
    .record('trace-armed', 'Remember the reels that armed', 'lookArmed', state('newlyArmed'))
    .record('trace-held', 'Remember the held cells', 'lookHeld', state('held'))
    .mechanic('combat', `Combat on the board: a reel that shows a ${launcherName} arms and turns ${wildName}; while any reel is armed, every ${planeName} is shot down, becomes a ${wildName} and adds a multiplier.`));

  const pays = mechanics.add(new Flow()
    .use('combat', 'Combat', combat)
    .use('clusters', 'Pay the groups on the board', mechanics.add(clusterPays(mechanics, clusters)))
    .write('held', 'A held cell stays on the board when its group pays', 'removed',
      foldOver('held', state('removed'), when(eq(item, num(1)), except(state('acc'), only(place)), state('acc')), numbered))
    .mechanic('combat-pays', 'Looks at a board: the combat comes first, then the groups pay. Held cells stay on the board.'));

  const payStep = new Flow()
    .write('spin', 'Add the groups to what the spin has paid', 'spinCoins', add(state('spinCoins'), state('clusterCoins')))
    .record('trace-spin', 'Remember which spin the step belongs to', 'stepSpins', sub(length('spinBoards'), num(1)))
    .record('trace-win', 'Remember what the groups paid', 'stepWins', state('clusterCoins'));
  for (const key of clusterTrace) payStep.record(`trace-${key}`, 'Remember the groups that paid', stepHits(key), state(key));
  const paying = mechanics.add(payStep.mechanic('pay-step', 'Pays one step of a cascade: its groups are added to what the spin has paid.'));

  /** The cascade of a spin on one set of reels: what leaves the board is filled again from those reels. */
  const cascading = (kind: 'paid' | 'free', reels: ReelSet) => mechanics.add(cascade(mechanics, {
    name: `${kind}-cascade`, maxSteps: model.cascade.max_steps,
    pays: { mechanic: pays, label: 'Combat, then pay the groups on the board', coins: 'clusterCoins' },
    onStep: { mechanic: paying, label: 'Pay the step' },
    tumble: { label: 'Remove what paid and fill the board', mechanic: mechanics.add(new Flow()
      .use('tumble', 'Remove what paid; the rest falls and the reels fill the board',
        mechanics.add(tumble(mechanics, { name: `${kind}-tumble`, columns: COLUMNS, rows: ROWS, refill: { reels }, carried: [held] })))
      .write('scatters', `Count the ${scatterName}s that dropped in: those on the board now, less those that stayed`, 'scatterCount',
        add(state('scatterCount'), sub(countOver('board', eq(item, str(SCATTER))), countOver('survivors', eq(item, str(SCATTER))))))
      .record('trace-held', 'Remember the held cells once the symbols have fallen', 'stepHeld', state('held'))
      .mechanic(`${kind}-refill`, `Fills the board after a step and counts the ${scatterName}s that dropped in.`)) },
  }));

  /** Adds the steps every spin ends with once its board has landed: the cascade, and what the spin pays. */
  const settle = (flow: Flow, cascadeOfSpin: string) => flow
    .write('scatters', `Count the ${scatterName}s the spin landed with`, 'scatterCount', countOver('board', eq(item, str(SCATTER))))
    .record('trace-board', 'Remember the board', 'spinBoards', state('board'))
    .write('fresh', 'The spin has paid nothing yet', 'spinCoins', num(0))
    .use('cascade', 'Cascade', cascadeOfSpin)
    .write('win', 'The spin pays its groups × the sum of its multipliers, or × 1 without any', 'spinWin',
      mul(state('spinCoins'), max(num(1), state('multiplierSum'))))
    .write('round', `Add it to the round, which pays at most ${model.max_win} bets`, 'roundCoins',
      min(state('capCoins'), add(state('roundCoins'), state('spinWin'))))
    .record('trace-scatters', `Remember the ${scatterName}s of the spin`, 'spinScatters', state('scatterCount'))
    .record('trace-groups', 'Remember what its groups paid', 'spinGroupCoins', state('spinCoins'))
    .record('trace-multiplier', 'Remember its multiplier', 'spinMultipliers', state('multiplierSum'))
    .record('trace-win', 'Remember what it won', 'spinWins', state('spinWin'))
    .record('trace-total', 'Remember what the round has paid so far', 'spinTotals', state('roundCoins'))
    .record('trace-armed', 'Remember the armed reels', 'spinArmed', state('armed'));

  const raid = tables.air_raid;
  const raidPlane = () => mechanics.add(new Flow()
    .draw('hit', 'Is the plane shot down?', 'raidHit', [[1, raid.hit_weight], [0, raid.miss_weight]])
    .either('shot', 'Shot down?', eq(state('raidHit'), str(1)), path => path
      .draw('reel', 'The reel it falls on', 'raidReel', Array.from({ length: COLUMNS }, (_, column) => [column, 1] as const))
      .draw('row', 'The row it falls on', 'raidRow', Array.from({ length: ROWS }, (_, row) => [row, 1] as const))
      .write('cell', 'The cell it falls on', 'raidCell', add(mul(toNumber(state('raidReel')), num(ROWS)), toNumber(state('raidRow'))))
      .use('multiplier', 'It leaves a multiplier', multiplier)
      .write('wild', `The cell becomes a ${wildName}`, 'board', mapOver('board', when(eq(place, state('raidCell')), str(WILD), item), { index: 'place' }))
      .record('trace-cell', 'Remember the cell', 'raidCells', state('raidCell')))
    .mechanic('raid-plane', `One plane of an air raid: if it is shot down it falls on a random cell, which becomes a ${wildName}, and leaves a multiplier.`));
  const airRaid = () => mechanics.add(new Flow()
    .record('trace-board', 'Remember the board before the raid', 'raidBoards', state('board'))
    .write('fresh', 'No plane has been shot down yet', 'drawnMultipliers', emptyArray())
    .draw('squadron', 'How many planes fly over', 'raidPlanes', raid.squadrons.map(({ planes, weight }) => [planes, weight] as const))
    // One pass more than the largest squadron, so that the limit is never what ends the loop.
    .repeat('plane', 'Every plane of the squadron', Math.max(...raid.squadrons.map(({ planes }) => planes)) + 1, raidPlane(),
      { until: gte(iteration('plane'), toNumber(state('raidPlanes'))), bodyLabel: 'One plane' })
    .record('trace-squadron', 'Remember the squadron', 'raidSquadrons', toNumber(state('raidPlanes')))
    .write('trace-multipliers', 'Remember the multipliers its planes left', 'raidMultipliers', state('drawnMultipliers'))
    .mechanic('air-raid', `An air raid: a squadron flies over, and every plane shot down falls on a random cell, turns it ${wildName} and leaves a multiplier.`));

  const spin = new Flow();
  if (feature === 'chance')
    spin.draw('force', `Does the spin bring its ${placed} ${scatterName}s?`, 'forced', [[1, model.buy.chance.force_weight], [0, model.buy.chance.pass_weight]]);
  spin.use('reels', 'Every reel stops', mechanics.add(spinReels(paidReels, mechanics)));
  if (feature === 'air-raid') spin.use('raid', 'Air raid', airRaid());
  else if (!tier)
    spin.draw('fire', 'Does an air raid come?', 'raidFires', [[1, raid.fire_weight], [0, raid.quiet_weight]])
      .onlyIf('raid', 'Air raid', eq(state('raidFires'), str(1)), airRaid());
  if (placed) {
    const scattered = mapOver('board', when(eq(at('placedScatters', place), num(1)), str(SCATTER), item), { index: 'place' });
    spin.write('place', `${placed} ${scatterName}s are placed along the top of the board`, 'board',
      feature === 'chance' ? when(eq(state('forced'), str(1)), scattered, state('board')) : scattered);
  }
  const spinName = { spin: 'paid-spin', chance: 'chance-spin', 'air-raid': 'air-raid-spin', standard: 'bought-spin', elite: 'bought-spin', super: 'bought-spin' }[feature];
  const spinText = {
    spin: 'One paid spin: the reels stop, an air raid may come, and the board cascades.',
    chance: `A spin bought for a better chance of free spins: sometimes ${placed} ${scatterName}s are placed on its board.`,
    'air-raid': 'A spin bought with an air raid that is sure to come.',
    standard: '', elite: '', super: '',
  }[feature] || `The spin that opens bought free spins: no air raid comes, and ${placed} ${scatterName}s are placed on its board.`;
  const playedSpin = mechanics.add(settle(spin, cascading('paid', paidReels)).mechanic(spinName, spinText));

  const freeSpin = mechanics.add(settle(new Flow()
    .write('use', 'Use up one free spin', 'freeSpins', sub(state('freeSpins'), num(1)))
    .write('played', 'Count it', 'freeSpinsPlayed', add(state('freeSpinsPlayed'), num(1)))
    .use('reels', 'Every reel stops', mechanics.add(spinReels(freeReels, mechanics)))
    .write('giants', `Every cell of an armed reel is a ${wildName}`, 'board', mapOver('board', when(eq(at('armed', reel), num(1)), str(WILD), item), { index: 'place' }))
    .write('held', 'Armed reels are held: they stay through every cascade', 'held', mapOver('held', at('armed', reel), numbered))
    .write('counts', 'An armed reel counts as one cell of a group', 'cellCounts', giantCounts), cascading('free', freeReels))
    .write('retrigger', `${TRIGGER}+ ${scatterName}s award more free spins (${awardText}), unless the round has reached its limit`, 'freeSpins',
      when(capped, state('freeSpins'), add(state('freeSpins'), awarded)))
    .mechanic('free-spin', 'One free spin: armed reels are giant wilds that stay, the board cascades, and the multiplier keeps growing from spin to spin.'));
  const freeSpins = mechanics.add(new Flow()
    .write('bonus', 'The free spins begin', 'bonus', num(1))
    .write('armed', 'The reels armed at the start', 'armed', state('startArmed'))
    .write('multiplier', 'The multiplier the free spins start with', 'multiplierSum', state('startMultiplier'))
    // Scatters can award more spins without end; the limit is this model's, with one pass of slack for the loop.
    .repeat('free-spin', `Free spins until none are left, the round has reached its limit, or ${LIMIT} have been played`, LIMIT + 1, freeSpin,
      { until: or(lte(state('freeSpins'), num(0)), capped, gte(state('freeSpinsPlayed'), num(LIMIT))), bodyLabel: 'Free spin' })
    .mechanic('free-spins', 'The free spins: armed reels and the multiplier are kept from spin to spin. They end early only when the round reaches the most it may pay.'));

  const root = new Flow().use('spin', { spin: 'Paid spin', chance: 'Chance spin', 'air-raid': 'Air raid spin' }[feature as string] ?? 'Opening spin', playedSpin);
  if (tier)
    root.write('award', `The bought free spins: ${awardFor(tier.scatters)}`, 'freeSpins', num(awardFor(tier.scatters)))
      .onlyIf('bonus', 'The free spins start, unless the round has reached its limit', lt(state('roundCoins'), state('capCoins')), freeSpins);
  else
    root.write('award', `${scatterName}s award free spins: ${awardText}`, 'freeSpins', awarded)
      .onlyIf('trigger', `${TRIGGER}+ ${scatterName}s start the free spins`, and(gte(state('scatterCount'), num(TRIGGER)), lt(state('roundCoins'), state('capCoins'))), freeSpins);
  root.write('credits', `Round win as a multiple of ${feature === 'spin' ? 'the total bet' : 'the price paid'}`, 'winCredits', div(state('roundCoins'), state('roundCost')))
    .sink('round', `Round payout & RTP · at most ${model.max_win} bets`, 'winCredits', Math.ceil(CAP_COINS / ROUND_COST));

  const featureText = {
    spin: '', chance: ', a spin bought for a better chance of free spins', 'air-raid': ', a spin bought with a sure air raid',
    standard: ', free spins bought', elite: ', elite free spins bought', super: ', super free spins bought',
  }[feature];
  const boughtFor = feature === 'spin' ? '' : ` for ${ROUND_COST / TOTAL_BET} bets`;
  return gameGraph({
    id: graphId, name: `${model.game_metadata.name} · ${mode}${feature === 'spin' ? '' : ` · ${feature}`}`,
    description: `${model.game_metadata.name} in its ${mode} mode${featureText}${boughtFor}: ${model.source}.`,
    root, mechanics,
    initialState: {
      totalBet: TOTAL_BET, roundCost: ROUND_COST, capCoins: CAP_COINS, winCredits: 0, roundCoins: 0, spinCoins: 0, spinWin: 0,
      multiplierSum: 0, scatterCount: 0, freeSpins: 0, freeSpinsPlayed: 0, bonus: 0, planeCount: 0, drawnMultiplier: '0', drawnMultipliers: [],
      held: zeros(CELLS), armed: zeros(COLUMNS), newlyArmed: zeros(COLUMNS), cellCounts: Array.from({ length: CELLS }, () => 1),
      startArmed: Array.from({ length: COLUMNS }, (_, column) => column % 2 === 0 && column / 2 < (tier?.armed_reels ?? 0) ? 1 : 0),
      startMultiplier: tier?.multiplier ?? 0,
      freeSpinAwards: Array.from({ length: MOST_SCATTERS + 1 }, (_, scatters) => awardFor(scatters)),
      ...(placed ? { placedScatters } : {}), ...(feature === 'chance' ? { forced: '0' } : {}),
      ...(tier ? {} : { raidFires: '0', raidPlanes: '0', raidHit: '0', raidReel: '0', raidRow: '0', raidCell: 0 }),
      board: Array.from({ length: CELLS }, () => '0'), ...Object.fromEntries(Array.from({ length: COLUMNS }, (_, column) => [`stop${column}`, '0'])),
      ...reelState(paidReels), ...reelState(freeReels), ...clusterState(clusters), ...cascadeState({ columns: COLUMNS, rows: ROWS, carried: [held], refill: { reels: paidReels } }),
      ...Object.fromEntries([...spinTrace, ...lookTrace, ...stepTrace, ...clusterTrace.map(stepHits), ...(tier ? [] : raidTrace)].map(key => [key, []])),
    },
    tables: gameTables({ names, wild: WILD, scatter: SCATTER, totalBet: TOTAL_BET, roundCost: ROUND_COST, rows: ROWS, columns: COLUMNS,
      clusterPays: model.cluster_paytable }),
  });
}

export { fullModel as leMilitareModel };
