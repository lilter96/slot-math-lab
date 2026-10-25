import fullModel from './model.json' with {type: 'json'};
import {add, div, gte, lte, num, state, sub, when} from '../shared/graph/expr';
import {Flow, gameGraph, Mechanics} from '../shared/graph/flow';
import {lineSpin, lineSpinState} from '../shared/graph/mechanics/line-spin';
import {linePayTable, paylineState} from '../shared/graph/mechanics/paylines';
import {lineEvaluation, lineEvaluationState} from '../shared/graph/mechanics/line-evaluation';
import {reelState, spinReels, type ReelSet} from '../shared/graph/mechanics/reels';
import {scatterPay, scatterPayTable} from '../shared/graph/mechanics/scatter';
import {gameTables} from '../shared/graph/tables';

export type AncientDragonModel = typeof fullModel;

const ROWS = 3;

/**
 * Ancient Dragon as a graph: 5 × 3 reels, 25 paylines, a mystery symbol, scatter pays and retriggering free spins.
 * Everything specific to the game is read from `model`; the default is the released game.
 */
export function createAncientDragonGraph(model: AncientDragonModel = fullModel, graphId = 'ancient-dragon'): Record<string, unknown> {
    const names: readonly string[] = model.symbols.regular;
    const id = (name: string) => names.indexOf(name);
    const WILD = id(model.symbols.wild), SCATTER = id(model.symbols.scatter), MYSTERY = id('INNER');
    const TOTAL_BET = model.game_metadata.cost_to_play;
    const FREE_SPINS = model.feature.free_spins_per_trigger;
    const TRIGGER = model.feature.trigger.min_count;
    // How often each symbol appears on the mystery strip is its weight in the mystery draw.
    const mysteryWeights = new Map<number, number>();
    for (const name of model.inner_reel_strip.reel1) mysteryWeights.set(id(name), (mysteryWeights.get(id(name)) ?? 0) + 1);

    const mechanics = new Mechanics();
    const reels: ReelSet = {
        name: 'spin-reels', stripKey: 'reel', rows: ROWS,
        strips: Object.values(model.reel_strips).map(strip => strip.map(id))
    };
    const pays = Object.fromEntries(Object.entries(model.paytable).map(([name, row]) => [id(name), row]));
    const scatterPays = scatterPayTable(model.scatter_paytable.YINYANG, reels.strips.length * ROWS);

    const spin = mechanics.add(lineSpin({
        reels: mechanics.add(spinReels(reels)),
        paylines: mechanics.add(lineEvaluation(model.paylines, ROWS)),
        scatterPay: mechanics.add(scatterPay()),
        scatter: SCATTER, mystery: {placeholder: MYSTERY, outcomes: [...mysteryWeights]},
    }));
    // The original game has no win cap. The sink's cap is the most the round can pay at all, so it never cuts a win.
    const bestSpin = Math.max(...Object.values(pays).flatMap(row => Object.values(row))) * model.paylines.length / TOTAL_BET + Math.max(...scatterPays);
    const roundCap = Math.ceil(bestSpin * (1 + model.limits.free_spin_cap));
    const awarded = when(gte(state('scatterCount'), num(TRIGGER)), num(FREE_SPINS), num(0));

    const freeSpin = mechanics.add(new Flow()
        .write('use', 'Use one free spin', 'freeSpins', sub(state('freeSpins'), num(1)))
        .use('spin', 'Free spin', spin)
        .write('retrigger', `${TRIGGER}+ scatters add ${FREE_SPINS} more free spins`, 'freeSpins', add(state('freeSpins'), awarded))
        .record('trace-left', 'Remember the free spins left', 'spinFreeSpinsLeft', state('freeSpins'))
        .mechanic('free-spin', 'One free spin, played like a paid spin. It can retrigger the feature.'));
    const freeSpins = mechanics.add(new Flow()
        .write('award', `Award ${FREE_SPINS} free spins`, 'freeSpins', num(FREE_SPINS))
        .repeat('play', 'Play until no free spins are left', model.limits.free_spin_cap, freeSpin, {
            until: lte(state('freeSpins'), num(0)),
            bodyLabel: 'Free spin'
        })
        .mechanic('free-spins', `The free-spins feature: ${FREE_SPINS} spins, extended by every retrigger.`));

    const root = new Flow()
        .use('base-spin', 'Paid spin · 5 reels, 25 lines', spin)
        .record('trace-left', 'Remember the free spins awarded', 'spinFreeSpinsLeft', awarded)
        .onlyIf('trigger', `${TRIGGER}+ scatters start the free spins`, gte(state('scatterCount'), num(TRIGGER)), freeSpins)
        .write('credits', 'Round win as a multiple of the total bet', 'winCredits', div(state('roundCoins'), state('roundCost')))
        .sink('round', 'Round payout & RTP', 'winCredits', roundCap);

    return gameGraph({
        id: graphId, name: model.game_metadata.name, description: `${model.game_metadata.name}: ${model.source}.`,
        root, mechanics,
        initialState: {
            totalBet: TOTAL_BET,
            roundCost: TOTAL_BET,
            scatterPays,
            freeSpins: 0,
            mysterySymbol: '',
            winCredits: 0,
            spinFreeSpinsLeft: [],
            ...reelState(reels), ...Object.fromEntries(reels.strips.map((_, reel) => [`stop${reel}`, '0'])),
            ...paylineState(WILD, linePayTable({
                symbolCount: names.length,
                wild: WILD,
                pays
            })), ...lineSpinState(), ...lineEvaluationState(model.paylines, ROWS),
        },
        tables: gameTables({
            names,
            wild: WILD,
            scatter: SCATTER,
            linePays: model.paytable,
            scatterPays: model.scatter_paytable.YINYANG,
            totalBet: TOTAL_BET,
            roundCost: TOTAL_BET,
            paylines: model.paylines,
            rows: ROWS,
            columns: reels.strips.length
        }),
    });
}

export {fullModel as ancientDragonModel};
