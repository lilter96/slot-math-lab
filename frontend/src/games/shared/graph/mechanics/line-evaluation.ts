import {add, at, div, eq, floor, itemAt, mul, num, or, state, sub, toNumber, toText, when, type Expr} from '../expr';
import {Flow, type Mechanic} from '../flow';

const local = (name: string): Expr => ({exprType: 'local', name});
const bind = (name: string, value: Expr, body: Expr): Expr => ({exprType: 'let', name, value, body});

/** Read each needed cell once; the result packs the symbol and matched length. */
export function lineDescriptor(cells: Expr, reels: number,
                               readCell: (position: Expr) => Expr = position => toNumber(itemAt(state('board'), position))): Expr {
    const width = reels + 1, wild = toNumber(state('wildSymbol'));
    const result = (symbol: Expr, count: number) => add(mul(symbol, num(width)), num(count));
    const read = (reel: number) => readCell(itemAt(cells, num(reel)));
    const known = (reel: number, symbol: Expr): Expr => {
        if (reel === reels) return result(symbol, reels);
        const name = `lineCell${reel}`, cell = local(name);
        return bind(name, read(reel), when(or(eq(cell, symbol), eq(cell, wild)),
            known(reel + 1, symbol), result(symbol, reel)));
    };
    const undecided = (reel: number): Expr => {
        const name = `lineCell${reel}`, cell = local(name);
        return bind(name, read(reel), reel === reels - 1 ? result(cell, reels)
            : when(eq(cell, wild), undecided(reel + 1), known(reel + 1, cell)));
    };
    return undecided(0);
}

const symbolOf = (descriptor: Expr, reels: number) => floor(div(descriptor, num(reels + 1)));
const countOf = (descriptor: Expr, reels: number) => sub(descriptor, mul(symbolOf(descriptor, reels), num(reels + 1)));
const payout = (descriptor: Expr, reels: number): Expr => bind('lineResult', descriptor,
    itemAt(itemAt(state('linePays'), symbolOf(local('lineResult'), reels)), countOf(local('lineResult'), reels)));
const mapLines = (body: Expr, maxItems: number): Expr => ({
    exprType: 'map',
    stateKey: 'paylineCells',
    itemName: 'line',
    itemType: 'Array',
    maxItems,
    body
});
const mapResults = (body: Expr, maxItems: number): Expr => ({
    exprType: 'map',
    stateKey: 'lineDescriptors',
    itemName: 'descriptor',
    itemType: 'Number',
    maxItems,
    body
});
const sum = (key: string, body: Expr, itemType: 'Array' | 'Number', maxItems: number): Expr =>
    ({
        exprType: 'aggregate',
        func: 'Sum',
        stateKey: key,
        itemName: itemType === 'Array' ? 'line' : 'item',
        itemType,
        maxItems,
        valueExpr: body
    });

/** Ordinary atoms and lexical expressions; analyses retain no per-line arrays. */
export function lineEvaluation(lines: readonly (readonly number[])[], rows: number,
                               readCell?: (position: Expr) => Expr): Mechanic {
    const reels = lines[0]?.length ?? 0;
    if (reels < 1 || lines.some(line => line.length !== reels || line.some(row => !Number.isInteger(row) || row < 0 || row >= rows)))
        throw new Error('Paylines must have the same positive width and valid row positions');
    const flow = new Flow();
    for (const key of ['linePayouts', 'lineSymbols', 'lineCounts', 'lineDescriptors'])
        flow.write(`clear-${key}`, `Clear ${key}`, key, state('emptyArray'));
    flow.either('played', 'Retain line details for a played round', state('traceEnabled'), played => played
            .write('evaluate', 'Evaluate every line once', 'lineDescriptors', mapLines(lineDescriptor(state('line'), reels, readCell), lines.length))
            .write('payouts', 'Payout of each line', 'linePayouts', mapResults(payout(state('descriptor'), reels), lines.length))
            .write('symbols', 'Symbol of each line', 'lineSymbols', mapResults(toText(symbolOf(state('descriptor'), reels)), lines.length))
            .write('counts', 'Matched length of each line', 'lineCounts', mapResults(countOf(state('descriptor'), reels), lines.length))
            .write('played-total', 'Sum the line payouts', 'lineCoins', sum('linePayouts', state('item'), 'Number', lines.length)),
        analysis => analysis.write('analysis-total', 'Sum line payouts without retaining line arrays', 'lineCoins',
            sum('paylineCells', payout(lineDescriptor(state('line'), reels, readCell), reels), 'Array', lines.length)));
    flow.write('last', 'Result of the last line', 'lineDescriptor', when(state('traceEnabled'),
        at('lineDescriptors', num(lines.length - 1)), lineDescriptor(at('paylineCells', num(lines.length - 1)), reels, readCell)))
        .write('last-symbol', 'Symbol of the last line', 'lineSymbol', toText(symbolOf(state('lineDescriptor'), reels)))
        .write('last-count', 'Matched length of the last line', 'lineCount', countOf(state('lineDescriptor'), reels))
        .write('last-payout', 'Payout of the last line', 'linePay', payout(state('lineDescriptor'), reels));
    return flow.mechanic('line-evaluation', 'Evaluate the leftmost matching run on each line; retain line details only when recording the played round.');
}

export const lineEvaluationState = (lines: readonly (readonly number[])[], rows: number,
                                    coordinate: (cell: number) => number = cell => cell) => ({
    paylineCells: lines.map(line => line.map((row, reel) => coordinate(reel * rows + row))),
    lineDescriptor: 0,
    lineDescriptors: [],
});
