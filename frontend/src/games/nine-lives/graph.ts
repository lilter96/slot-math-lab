import model from './model.json' with { type: 'json' };
import type { ExpressionAst as Expr } from '../../lib/expressionParser';

// These helpers author JSON. All round execution belongs to the existing graph atoms.
const n = (value: number): Expr => ({ exprType: 'constant', kind: 'Integer', value: String(value) });
const s = (value: string): Expr => ({ exprType: 'constant', kind: 'String', value });
const b = (value: boolean): Expr => ({ exprType: 'constant', kind: 'Boolean', value: String(value) });
const f = (key: string): Expr => ({ exprType: 'fieldAccess', target: 'state', path: [key] });
const binary = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'binary', op, left, right });
const compare = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'compare', op, left, right });
const call = (fn: string, ...args: Expr[]): Expr => ({ exprType: 'call', function: fn, args });
const choose = (condition: Expr, thenExpr: Expr, elseExpr: Expr): Expr => ({ exprType: 'if', condition, thenExpr, elseExpr });
const add = (a: Expr, c: Expr) => binary('Add', a, c);
const sub = (a: Expr, c: Expr) => binary('Sub', a, c);
const mul = (a: Expr, c: Expr) => binary('Mul', a, c);
const div = (a: Expr, c: Expr) => binary('Div', a, c);
const eq = (a: Expr, c: Expr) => compare('Eq', a, c);
const and = (...values: Expr[]) => values.reduce((a, c) => binary('And', a, c));
const or = (a: Expr, c: Expr) => binary('Or', a, c);
const not = (expr: Expr): Expr => ({ exprType: 'not', expr });
const at = (key: string, index: Expr) => call('index', f(key), index);
const len = (key: string) => call('length', f(key));
const has = (key: string, value: Expr) => call('contains', f(key), value);
const append = (key: string, value: Expr) => call('append', f(key), value);
const numeric = (expr: Expr) => call('toNumber', expr);
const iter = (id: string) => f(`__iter_${id}__`);
const empty = () => f('emptyArray');
const array = (values: Expr[]) => values.reduce((a, c) => call('append', a, c), empty());
const map = (key: string, body: Expr, itemType = 'String', indexName?: string): Expr => ({ exprType: 'map', stateKey: key, itemName: 'item', ...(indexName ? { indexName } : {}), itemType, body });
const filter = (key: string, predicate: Expr, itemType = 'Number'): Expr => ({ exprType: 'filter', stateKey: key, itemName: 'item', itemType, predicate });
const union = (key: string, initial: string, predicate = b(true)): Expr => ({ exprType: 'fold', stateKey: key, itemType: 'Number', itemName: 'item', accName: 'acc', init: f(initial),
  body: choose(and(predicate, not(call('contains', f('acc'), f('item')))), call('append', f('acc'), f('item')), f('acc')) });
const appendDisjoint = (key: string, initial: string, predicate = b(true)): Expr => ({ exprType: 'fold', stateKey: key, itemType: 'Number', itemName: 'item', accName: 'acc', init: f(initial),
  body: choose(predicate, call('append', f('acc'), f('item')), f('acc')) });
// A balanced JSON conditional selects an authored scalar slot; no dynamic
// field lookup or new runtime function is introduced.
const indexedChoice = (index: Expr, values: Expr[], first = 0): Expr => {
  if (values.length === 1) return values[0];
  const middle = Math.floor(values.length / 2);
  return choose(compare('Lt', index, n(first + middle)), indexedChoice(index, values.slice(0, middle), first), indexedChoice(index, values.slice(middle), first + middle));
};
const statePort = { state: { name: 'state', type: 'State' } };
type Node = { nodeType: string; id: string; [key: string]: unknown };
type Edge = { id: string; sourceNodeId: string; sourcePort: string; targetNodeId: string; targetPort: string };
type Mechanic = { name: string; description: string; nodes: Node[]; edges: Edge[]; expressions: Record<string, Expr> };

// A structural authoring helper: no symbol evaluation, random sampling or payouts run here.
class Graph {
  nodes: Node[] = [];
  edges: Edge[] = [];
  expressions: Record<string, Expr> = {};
  private tail: [string, string] | undefined;
  private deferred: Node[] = [];
  private connect(node: Node) {
    if (this.tail) this.edge(this.tail[0], this.tail[1], node.id);
    this.nodes.push(node);
    this.tail = [node.id, 'state'];
  }
  edge(sourceNodeId: string, sourcePort: string, targetNodeId: string) {
    this.edges.push({ id: `e${this.edges.length}`, sourceNodeId, sourcePort, targetNodeId, targetPort: 'state' });
  }
  write(id: string, key: string, expression: Expr) {
    this.expressions[id] = expression;
    this.connect({ nodeType: 'modifyState', id, outputKey: key, expressionId: id, inputs: statePort, outputs: statePort });
  }
  use(id: string, mechanicName: string) {
    this.connect({ nodeType: 'library', id, mechanicName, inputs: statePort, outputs: statePort });
  }
  draw(id: string, key: string, weights: number[], outcomes = weights.map((_, i) => i)) {
    this.connect({ nodeType: 'draw', id, stateWriteKey: key, inputs: statePort, outputs: statePort,
      drawWeights: weights.flatMap((weight, i) => weight > 0 ? [{ outcomeId: String(outcomes[i]), weight, value: 0 }] : []) });
  }
  when(id: string, condition: Expr, mechanicName: string) {
    this.expressions[id] = condition;
    this.connect({ nodeType: 'branch', id, conditionId: id, inputs: statePort,
      outputs: { true: { name: 'true', type: 'State' }, false: { name: 'false', type: 'State' } } });
    const body: Node = { nodeType: 'library', id: `${id}-body`, mechanicName, inputs: statePort, outputs: statePort };
    this.deferred.push(body);
    this.edge(id, 'true', body.id);
    const merge: Node = { nodeType: 'getState', id: `${id}-end`, inputs: statePort, outputs: statePort };
    this.edge(id, 'false', merge.id);
    this.edge(body.id, 'state', merge.id);
    this.nodes.push(merge);
    this.tail = [merge.id, 'state'];
  }
  loop(id: string, count: number, mechanicName: string, stop?: Expr, counterKey?: string) {
    if (stop) this.expressions[`${id}-stop`] = stop;
    this.connect({ nodeType: 'loop', id, maxIterations: count, ...(stop ? { stopConditionId: `${id}-stop` } : {}), inputs: statePort,
      outputs: { body: { name: 'body', type: 'State' }, exit: { name: 'exit', type: 'State' } } });
    const body: Node = { nodeType: 'library', id: `${id}-body`, mechanicName, inputs: statePort, outputs: statePort };
    this.deferred.push(body);
    if (counterKey) {
      const counterId = `${id}-counter`;
      this.expressions[counterId] = iter(id);
      this.deferred.push({ nodeType: 'modifyState', id: counterId, outputKey: counterKey, expressionId: counterId, inputs: statePort, outputs: statePort });
      this.edge(id, 'body', counterId);
      this.edge(counterId, 'state', body.id);
    } else this.edge(id, 'body', body.id);
    this.tail = [id, 'exit'];
  }
  finish(name: string, description: string): Mechanic {
    this.write('done', 'graphCursor', f('graphCursor'));
    // Exit precedes leaves inside loop bodies for the existing subgraph boundary convention.
    return { name, description, nodes: [...this.nodes, ...this.deferred], edges: this.edges, expressions: this.expressions };
  }
  snapshot(id: string, history: string, value: Expr) {
    this.write(id, history, choose(f('traceEnabled'), append(history, value), f(history)));
  }
}

const symbolNames = ['WILD', 'BONE', 'FISH', 'YARN', 'CANDLE', 'CAT', 'COIN', 'SCATTER'] as const;
const payingSymbols = ['BONE', 'FISH', 'YARN', 'CANDLE', 'CAT'] as const;
const scanOrder = Array.from({ length: 30 }, (_, i) => (i % 5) * 6 + Math.floor(i / 5));

export function createNineLivesGraph(entry: 'base' | 'buy' = 'base'): Record<string, unknown> {
  const mechanics: Record<string, Mechanic> = {};
  const save = (graph: Graph, name: string, description: string) => { mechanics[name] = graph.finish(name, description); return name; };

  // Geometry is immutable data; neighbor eligibility is authored scalar AST.
  const neighbors = new Graph();
  neighbors.write('position', 'position', at('queue', f('queueIndex')));
  const neighbor = at('adjacentPositions', add(mul(f('position'), n(4)), f('direction')));
  const discoverable = and(compare('Gte', neighbor, n(0)),
    or(eq(at('board', neighbor), f('targetSymbol')), and(eq(at('board', neighbor), s('0')), not(has('usedWilds', neighbor)))),
    not(has('visited', neighbor)), not(call('contains', f('acc'), neighbor)));
  neighbors.write('discover', 'queue', { exprType: 'fold', stateKey: 'directions', itemType: 'Number', itemName: 'direction', accName: 'acc', init: f('queue'),
    body: choose(discoverable, call('append', f('acc'), neighbor), f('acc')) });
  save(neighbors, 'queue-neighbors', 'Read four authored adjacency entries in source discovery order.');

  const claim = new Graph();
  claim.write('payout', 'evalWin', add(f('evalWin'), f('componentPay')));
  claim.write('positions', 'winningPositions', choose(f('traceEnabled'), appendDisjoint('queue', 'winningPositions'), f('winningPositions')));
  claim.write('wilds', 'usedWilds', choose(compare('Gt', f('wildCount'), n(0)), appendDisjoint('queue', 'usedWilds', eq(at('board', f('item')), s('0'))), f('usedWilds')));
  for (const [key, value] of Object.entries({ hitSymbols: f('targetSymbol'), hitSizes: len('queue'), hitPayouts: f('componentPay'), hitPositions: f('queue') }))
    claim.write(`record-${key}`, key, key === 'hitSymbols' ? append(key, value) : choose(f('traceEnabled'), append(key, value), f(key)));
  save(claim, 'claim-component', 'Award a paying component and claim only its participating Wilds.');

  const component = new Graph();
  component.write('queue', 'queue', array([f('componentStart')]));
  component.loop('breadth-first', 30, 'queue-neighbors', compare('Gte', iter('breadth-first'), len('queue')), 'queueIndex');
  // Discovery rejects visited cells and duplicate queue entries, so the two
  // arrays are disjoint; claiming similarly rejects previously claimed Wilds.
  component.write('visited', 'visited', appendDisjoint('queue', 'visited'));
  component.write('size', 'componentSize', len('queue'));
  component.write('pay', 'componentPay', choose(compare('Gte', f('componentSize'), n(5)),
    numeric(at('clusterPaytable', add(mul(sub(numeric(f('targetSymbol')), n(1)), n(26)), sub(f('componentSize'), n(5))))), n(0)));
  component.when('paying', compare('Gt', f('componentPay'), n(0)), 'claim-component');
  save(component, 'connected-component', 'Bounded breadth-first traversal and an authored paytable lookup.');

  const componentStep = new Graph();
  componentStep.write('start', 'componentStart', at('symbolCandidates', f('componentIndex')));
  // If every vertex has neighbor-degree sum below four, a connected set
  // has at most four vertices (a short path or a three-leaf star). Thus every
  // paying set has an internal seed passing this necessary bound. Wild-free
  // untraced rounds can use that seed without changing payout or claiming.
  const neighborDegreeSum = Array.from({ length: 4 }, (_, direction) => {
    const neighbor = at(`neighborPositions${direction}`, f('componentStart'));
    return choose(and(compare('Gte', neighbor, n(0)), eq(at('board', neighbor), f('targetSymbol'))), at('neighborDegrees', neighbor), n(0));
  }).reduce(add);
  componentStep.when('unvisited', and(not(has('visited', f('componentStart'))),
    or(or(f('traceEnabled'), compare('Gt', f('wildCount'), n(0))), and(compare('Gte', at('neighborDegrees', f('componentStart')), n(2)), compare('Gte', neighborDegreeSum, n(4))))), 'connected-component');
  save(componentStep, 'component-scan-step', 'Visit each real symbol cell once within a symbol sweep.');
  const candidateComponents = new Graph();
  candidateComponents.loop('components', 30, 'component-scan-step', compare('Gte', iter('components'), len('symbolCandidates')), 'componentIndex');
  save(candidateComponents, 'candidate-components', 'Visit candidate components only when their total size can reach the payout threshold.');
  const adjacencyBound = new Graph();
  adjacencyBound.write('edges', 'matchingEdges', choose(compare('Gt', f('wildCount'), n(0)), n(8), {
    exprType: 'fold', stateKey: 'symbolCandidates', itemName: 'item', itemType: 'Number', accName: 'acc', init: n(0), body: add(f('acc'), at('neighborDegrees', f('item'))) }));
  adjacencyBound.when('connected-size-possible', compare('Gte', f('matchingEdges'), n(8)), 'candidate-components');
  save(adjacencyBound, 'component-edge-bound', 'A five-cell connected set needs degree sum at least eight when there are no Wilds.');
  const symbolSweep = new Graph();
  symbolSweep.write('visited', 'visited', empty());
  symbolSweep.write('candidates', 'symbolCandidates', filter('potentialPositions', eq(at('board', f('item')), f('targetSymbol'))));
  symbolSweep.when('possible', compare('Gte', add(len('symbolCandidates'), sub(f('wildCount'), len('usedWilds'))), n(5)), 'component-edge-bound');
  save(symbolSweep, 'symbol-components', 'Enumerate all components for one symbol before considering the next symbol.');
  const symbolStep = new Graph();
  symbolStep.write('target', 'targetSymbol', at('symbolOrder', f('symbolIndex')));
  symbolStep.use('components', 'symbol-components');
  save(symbolStep, 'symbol-scan-step', 'Column-major first-occurrence symbol order; Wilds are not seeds.');
  const vanishTypes = new Graph();
  vanishTypes.write('matching', 'matchingPositions', choose(f('traceEnabled'), filter('scanOrder', has('hitSymbols', at('board', f('item')))), f('matchingPositions')));
  vanishTypes.write('union', 'winningPositions', choose(f('traceEnabled'), union('matchingPositions', 'winningPositions'),
    filter('scanOrder', or(has('hitSymbols', at('board', f('item'))), has('usedWilds', f('item'))))));
  save(vanishTypes, 'vanish-paying-types', 'Vanish winning components and every other cell of a paying symbol type; retain unrelated Wilds.');
  const evaluate = new Graph();
  evaluate.write('win', 'evalWin', n(0));
  for (const key of ['winningPositions', 'usedWilds', 'hitSymbols', 'hitSizes', 'hitPayouts', 'hitPositions']) evaluate.write(`reset-${key}`, key, empty());
  const degree = Array.from({ length: 4 }, (_, direction) => {
    const neighbor = at(`neighborPositions${direction}`, f('index'));
    const symbol = at('board', neighbor);
    return choose(and(compare('Gte', neighbor, n(0)), or(eq(symbol, f('item')), eq(symbol, s('0')))), n(1), n(0));
  }).reduce(add);
  evaluate.write('degrees', 'neighborDegrees', map('board', degree, 'String', 'index'));
  const standard = ['1', '2', '3', '4', '5'].map(symbol => eq(at('board', f('item')), s(symbol))).reduce(or);
  evaluate.write('potential', 'potentialPositions', filter('scanOrder', and(standard, compare('Gt', at('neighborDegrees', f('item')), n(0)))));
  evaluate.write('wild-count', 'wildCount', { exprType: 'aggregate', func: 'Count', stateKey: 'board', itemName: 'item', itemType: 'String', predicate: eq(f('item'), s('0')) });
  evaluate.write('symbol-cells', 'symbolCells', map('scanOrder', at('board', f('item')), 'Number'));
  evaluate.write('symbol-order', 'symbolOrder', { exprType: 'fold', stateKey: 'symbolCells', itemType: 'String', itemName: 'item', accName: 'acc', init: empty(), body:
    choose(and(['1', '2', '3', '4', '5'].map(symbol => eq(f('item'), s(symbol))).reduce(or), not(call('contains', f('acc'), f('item')))), call('append', f('acc'), f('item')), f('acc')) });
  evaluate.loop('symbols', 5, 'symbol-scan-step', compare('Gte', iter('symbols'), len('symbolOrder')), 'symbolIndex');
  evaluate.use('vanish', 'vanish-paying-types');
  save(evaluate, 'evaluate-components', 'Compose symbol sweeps, bounded connected components and exclusive Wild claiming.');

  // Stage column results and reconstruct the complete board in one map.
  const refill = new Graph();
  refill.draw('symbol', 'gravityValue', model.refillWeights);
  save(refill, 'refill-cell', 'Draw one replacement from the original refill weights.');
  const gravityCell = new Graph();
  gravityCell.write('survivor', 'gravityValue', choose(compare('Lt', f('gravityRow'), f('survivorCount')),
    at('gravityOriginal', at('survivors', sub(sub(f('survivorCount'), n(1)), f('gravityRow')))), s('0')));
  gravityCell.when('needs-refill', compare('Gte', f('gravityRow'), f('survivorCount')), 'refill-cell');
  save(gravityCell, 'gravity-cell', 'Copy one surviving cell or draw a vacancy, preserving bottom-to-top draw order.');
  const gravityColumn = new Graph();
  gravityColumn.write('positions', 'columnPositions', at('columnPositionsByIndex', f('gravityColumn')));
  gravityColumn.write('survivors', 'survivors', filter('columnPositions', not(has('winningPositions', f('item')))));
  gravityColumn.write('count', 'survivorCount', len('survivors'));
  for (let row = 0; row < 5; row++) {
    gravityColumn.write(`row-${row}`, 'gravityRow', n(row));
    gravityColumn.use(`cell-${row}`, 'gravity-cell');
    gravityColumn.write(`value-${row}`, `columnValue${row}`, f('gravityValue'));
  }
  gravityColumn.write('values', 'columnValues', array(Array.from({ length: 5 }, (_, row) => f(`columnValue${row}`))));
  gravityColumn.write('columns', 'gravityColumns', append('gravityColumns', f('columnValues')));
  save(gravityColumn, 'gravity-column', 'Prepare five bottom-to-top values without rebuilding the board for each cell.');
  const restoreBoard = new Graph();
  restoreBoard.write('restore', 'board', map('positions', call('index', at('gravityColumns', at('boardColumns', f('index'))), at('bottomRows', f('index'))), 'Number', 'index'));
  save(restoreBoard, 'restore-row-major', 'Transpose staged column values into a row-major board with one existing map.');
  const gravity = new Graph();
  gravity.write('original', 'gravityOriginal', f('board'));
  gravity.write('columns-reset', 'gravityColumns', empty());
  gravity.loop('columns', 6, 'gravity-column', undefined, 'gravityColumn');
  gravity.use('restore', 'restore-row-major');
  save(gravity, 'compact-and-refill', 'Stage stable columns in original RNG order, then build the board once.');

  const cascadeStep = new Graph();
  for (const [key, value] of Object.entries({ stepSpins: f('spinIndex'), stepBoardsBefore: f('board'), stepMultipliers: f('multiplier'),
    stepHitSymbols: f('hitSymbols'), stepHitSizes: f('hitSizes'), stepHitPayouts: f('hitPayouts'), stepHitTotals: map('hitPayouts', mul(f('item'), f('multiplier')), 'Number'), stepHitPositions: f('hitPositions'), stepVanished: f('winningPositions') }))
    cascadeStep.snapshot(`record-${key}`, key, value);
  cascadeStep.write('award', 'stepWin', call('min', mul(f('evalWin'), f('multiplier')), sub(f('capCoins'), f('roundCoins'))));
  cascadeStep.write('round', 'roundCoins', add(f('roundCoins'), f('stepWin')));
  cascadeStep.write('cluster', 'spinClusterCoins', add(f('spinClusterCoins'), f('stepWin')));
  cascadeStep.snapshot('record-win', 'stepWins', f('stepWin'));
  cascadeStep.use('refill', 'compact-and-refill');
  cascadeStep.snapshot('record-after', 'stepBoardsAfter', f('board'));
  cascadeStep.write('multiplier', 'multiplier', call('min', n(model.maxMultiplier), add(f('multiplier'), n(1))));
  cascadeStep.use('evaluate', 'evaluate-components');
  save(cascadeStep, 'cascade-step', 'Sequential capped award, stable refill and the next component evaluation.');
  const cascades = new Graph();
  cascades.use('evaluate', 'evaluate-components');
  cascades.loop('cascades', model.maxCascades, 'cascade-step', eq(f('evalWin'), n(0)));
  save(cascades, 'cascades', 'At most thirty winning steps; the final step still refills and evaluates.');

  const collect = new Graph();
  collect.write('positions', 'coinPositions', filter('positions', eq(at('board', f('item')), s('6'))));
  collect.write('values', 'coinValues', map('coinPositions', mul(numeric(at('prizes', f('item'))), n(model.baseCost)), 'Number'));
  collect.write('sum', 'spinCollectionCoins', call('min', { exprType: 'aggregate', func: 'Sum', stateKey: 'coinValues', itemName: 'item', itemType: 'Number', valueExpr: mul(f('item'), f('multiplier')) }, sub(f('capCoins'), f('roundCoins'))));
  collect.write('award', 'roundCoins', add(f('roundCoins'), f('spinCollectionCoins')));
  collect.write('wilds', 'board', map('board', choose(eq(f('item'), s('6')), s('0'), f('item'))));
  save(collect, 'collect-coins', 'Collect initial coin prizes once at the carried multiplier, then turn coins into Wilds for this spin.');

  for (const free of [false, true]) {
    const spin = new Graph();
    spin.write('cluster-reset', 'spinClusterCoins', n(0));
    spin.write('collection-reset', 'spinCollectionCoins', n(0));
    spin.write('coin-positions-reset', 'coinPositions', empty());
    spin.write('coin-values-reset', 'coinValues', empty());
    if (!free) spin.write('reset-multiplier', 'multiplier', n(1));
    else spin.write('consume-life', 'remaining', sub(f('remaining'), n(1)));
    for (let p = 0; p < 30; p++) spin.draw(`cell-${p}`, `cell${p}`, free ? model.bonusWeights : model.weights);
    spin.write('board', 'board', map('positions', indexedChoice(f('item'), Array.from({ length: 30 }, (_, p) => f(`cell${Math.floor(p / 6) + (p % 6) * 5}`))), 'Number'));
    spin.write('initial', 'initialBoard', f('board'));
    if (free) {
      for (let p = 0; p < 30; p++) spin.draw(`prize-${p}`, `prize${p}`, model.prizes.map(x => x[1]), model.prizes.map(x => x[0]));
      spin.write('prizes', 'prizes', map('positions', indexedChoice(f('item'), Array.from({ length: 30 }, (_, p) => f(`prize${Math.floor(p / 6) + (p % 6) * 5}`))), 'Number'));
      spin.use('collect', 'collect-coins');
    } else {
      spin.write('scatters', 'scatters', { exprType: 'aggregate', func: 'Count', stateKey: 'board', itemName: 'item', itemType: 'String', predicate: eq(f('item'), s('7')) });
    }
    spin.use('cascades', 'cascades');
    if (!free) {
      spin.write('final-multiplier', 'multiplier', n(1));
      spin.write('initial-lives', 'remaining', choose(and(compare('Gte', f('scatters'), n(4)), compare('Lt', f('roundCoins'), f('capCoins'))), n(model.freeSpins), n(0)));
    } else spin.write('cap-ends-bonus', 'remaining', choose(compare('Gte', f('roundCoins'), f('capCoins')), n(0), f('remaining')));
    for (const [key, value] of Object.entries({ boards: f('initialBoard'), finalBoards: f('board'), winHistory: add(f('spinClusterCoins'), f('spinCollectionCoins')),
      clusterWinHistory: f('spinClusterCoins'), collectionWinHistory: f('spinCollectionCoins'), coinPositionHistory: f('coinPositions'), coinValueHistory: f('coinValues'),
      multiplierHistory: f('multiplier'), remainingHistory: f('remaining'), roundWinHistory: f('roundCoins') })) spin.snapshot(`record-${key}`, key, value);
    spin.write('component-total', free ? 'freeCoins' : 'baseCoins', add(f(free ? 'freeCoins' : 'baseCoins'), add(f('spinClusterCoins'), f('spinCollectionCoins'))));
    spin.write('next-spin', 'spinIndex', add(f('spinIndex'), n(1)));
    save(spin, free ? 'free-spin' : 'base-spin', free ? 'Original free-spin symbols, prize draws, collector and carried cascade multiplier.' : 'Original paid-spin symbols and scatter count on the initial board.');
  }
  const bonus = new Graph();
  bonus.write('start', 'bonusTriggered', b(true));
  bonus.write('lives', 'remaining', n(model.freeSpins));
  bonus.write('reset-multiplier', 'multiplier', n(1));
  bonus.loop('free-spins', model.freeSpins, 'free-spin', compare('Gte', f('roundCoins'), f('capCoins')));
  bonus.write('completed', 'bonusCompleted', b(true));
  save(bonus, 'bonus', 'Nine lives with no retrigger, carried multiplier and round cap termination.');
  const base = new Graph();
  base.use('spin', 'base-spin');
  base.when('trigger', and(compare('Gte', f('scatters'), n(4)), compare('Lt', f('roundCoins'), f('capCoins'))), 'bonus');
  save(base, 'paid-round', 'Resolve all base cascades before testing initial scatters and the cap.');
  const entryGraph = new Graph();
  entryGraph.when('purchase', f('entryBuy'), 'bonus');
  entryGraph.when('paid', not(f('entryBuy')), 'paid-round');
  save(entryGraph, 'round-entry', 'Choose purchased or paid entry from an authored state flag.');
  const root = new Graph();
  root.use('round', 'round-entry');
  root.write('credits', 'winCredits', div(f('roundCoins'), n(model.baseCost)));
  const authored = root.finish('nine-lives', 'Nine Lives, authored entirely from the existing atoms and expression AST.');
  authored.nodes.push({ nodeType: 'metricsSink', id: 'round-metrics', winStateKey: 'winCredits', winCap: model.maxWinX, inputs: statePort, outputs: {} });
  authored.edges.push({ id: 'metrics', sourceNodeId: 'done', sourcePort: 'state', targetNodeId: 'round-metrics', targetPort: 'state' });

  const histories = ['boards', 'finalBoards', 'winHistory', 'clusterWinHistory', 'collectionWinHistory', 'coinPositionHistory', 'coinValueHistory', 'multiplierHistory', 'remainingHistory', 'roundWinHistory',
    'stepSpins', 'stepBoardsBefore', 'stepBoardsAfter', 'stepMultipliers', 'stepWins', 'stepHitSymbols', 'stepHitSizes', 'stepHitPayouts', 'stepHitTotals', 'stepHitPositions', 'stepVanished'];
  const initialState: Record<string, unknown> = { emptyArray: [], traceEnabled: false, graphCursor: 0, positions: Array.from({ length: 30 }, (_, i) => i), scanOrder,
    columnPositionsByIndex: Array.from({ length: 6 }, (_, column) => Array.from({ length: 5 }, (_, row) => row * 6 + column)),
    boardColumns: Array.from({ length: 30 }, (_, p) => p % 6), bottomRows: Array.from({ length: 30 }, (_, p) => 4 - Math.floor(p / 6)),
    ...Object.fromEntries(Array.from({ length: 4 }, (_, direction) => [`neighborPositions${direction}`, Array.from({ length: 30 }, (_, p) => [p >= 6 ? p - 6 : -1, p < 24 ? p + 6 : -1, p % 6 > 0 ? p - 1 : -1, p % 6 < 5 ? p + 1 : -1][direction])])),
    neighborDegrees: Array(30).fill(0), wildCount: 0, matchingEdges: 0, potentialPositions: [], directions: [0, 1, 2, 3],
    adjacentPositions: Array.from({ length: 30 }, (_, p) => [p >= 6 ? p - 6 : -1, p < 24 ? p + 6 : -1, p % 6 > 0 ? p - 1 : -1, p % 6 < 5 ? p + 1 : -1]).flat(),
    clusterPaytable: payingSymbols.flatMap(name => Object.values(model.paytable[name])).map(String),
    baseCost: model.baseCost, freeSpins: model.freeSpins, maxCascades: model.maxCascades, maxMultiplier: model.maxMultiplier, targetRTP: model.targetRTP, targetRtpPercent: model.targetRTP * 100,
    roundCoins: 0, capCoins: model.maxWinX * model.baseCost, baseCoins: 0, freeCoins: 0, spinIndex: 0, multiplier: 1, remaining: 0, scatters: 0,
    bonusTriggered: false, bonusCompleted: false, entryBuy: entry === 'buy', entryCost: entry === 'buy' ? model.buyCost : 1,
    targetSymbol: '0', gravityValue: '0',
    position: 0, componentStart: 0, componentSize: 0, componentPay: 0, evalWin: 0,
    queueIndex: 0, symbolIndex: 0, componentIndex: 0, gravityColumn: 0, gravityRow: 0, survivorCount: 0,
    ...Object.fromEntries(Array.from({ length: 5 }, (_, row) => [`columnValue${row}`, '0'])),
    spinClusterCoins: 0, spinCollectionCoins: 0, stepWin: 0, winCredits: 0,
    board: Array(30).fill('0'), initialBoard: [], gravityOriginal: [], queue: [], visited: [], winningPositions: [], usedWilds: [],
    hitSymbols: [], hitSizes: [], hitPayouts: [], hitPositions: [], symbolCells: [], symbolOrder: [], symbolCandidates: [], matchingPositions: [], survivors: [], columnPositions: [], columnValues: Array(5).fill('0'), gravityColumns: Array.from({ length: 6 }, () => Array(5).fill('0')), coinPositions: [], coinValues: [], prizes: [],
    ...Object.fromEntries(histories.map(key => [key, []])) };
  return { schemaVersion: '1.0.0', id: `nine-lives-${entry}`, name: 'Nine Lives', description: authored.description, initialState,
    nodes: authored.nodes, edges: authored.edges, expressions: authored.expressions, mechanics,
    stateSchema: Object.entries(initialState).filter(([, value]) => ['number', 'string', 'boolean'].includes(typeof value)).map(([name, value]) => ({ name, type: typeof value })),
    symbols: symbolNames.map((name, i) => ({ id: String(i), name, kind: i === 0 ? 'Wild' : i === 7 ? 'Bonus' : 'Standard' })),
    paytables: [{ id: 'nine-lives-paytable', entries: payingSymbols.map((name, i) => ({ symbolId: String(i + 1), counts: Object.keys(model.paytable[name]).map(Number), payouts: Object.values(model.paytable[name]).map(value => String(value / model.baseCost)) })) }],
    boardConfig: { rows: 5, columns: 6 } };
}

export { model as nineLivesReference };
