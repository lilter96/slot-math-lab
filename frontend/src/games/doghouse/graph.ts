import model from './model.json' with { type: 'json' };
import type { ExpressionAst as Expr } from '../../lib/expressionParser';

// Authoring helpers only: the result is ordinary, editable graph JSON. No spin or payout is calculated here.
const num = (value: number): Expr => ({ exprType: 'constant', kind: 'Integer', value: String(value) });
const str = (value: string): Expr => ({ exprType: 'constant', kind: 'String', value });
const field = (key: string): Expr => ({ exprType: 'fieldAccess', target: 'state', path: [key] });
const param = (key: string): Expr => ({ exprType: 'fieldAccess', target: 'state', path: ['expressions', key] });
const bin = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'binary', op, left, right });
const cmp = (op: string, left: Expr, right: Expr): Expr => ({ exprType: 'compare', op, left, right });
const call = (fn: string, ...args: Expr[]): Expr => ({ exprType: 'call', function: fn, args });
const iff = (condition: Expr, thenExpr: Expr, elseExpr: Expr): Expr => ({ exprType: 'if', condition, thenExpr, elseExpr });
const sum = (values: Expr[]) => values.reduce((a, b) => bin('Add', a, b), num(0));
const and = (values: Expr[]) => values.reduce((a, b) => bin('And', a, b));
const array = (values: Expr[]) => values.reduce((a, b) => call('append', a, b), field('emptyArray'));
const at = (key: string, index: Expr) => call('index', field(key), index);
const numeric = (e: Expr) => call('toNumber', e);
const text = (e: Expr) => call('toString', e);
const ports = { state: { name: 'state', type: 'State' } };
type Node = { nodeType: string; id: string; label?: string; [key: string]: unknown };
type Edge = { id: string; sourceNodeId: string; sourcePort: string; targetNodeId: string; targetPort: string };
type Mechanic = { name: string; description: string; nodes: Node[]; edges: Edge[]; expressions: Record<string, Expr> };
function chain(nodes: Node[]): Edge[] { return nodes.slice(1).map((n, i) => ({ id: `edge-${i}`, sourceNodeId: nodes[i].id, sourcePort: 'state', targetNodeId: n.id, targetPort: 'state' })); }
function modify(id: string, label: string, outputKey: string, expressionId = id): Node { return { nodeType: 'modifyState', id, label, outputKey, expressionId, inputs: ports, outputs: ports }; }
function library(id: string, mechanicName: string, label: string, parameters: Record<string, string> = {}): Node { return { nodeType: 'library', id, label, mechanicName, parameters, inputs: ports, outputs: ports }; }

function lineMechanic(): Mechanic {
  const symbol = (col: number) => at('board', param(`p${col}`));
  const target = symbol(0);
  const matches = (col: number) => bin('Or', cmp('Eq', symbol(col), target), cmp('Eq', symbol(col), str('2')));
  const count = iff(and([matches(1), matches(2)]), iff(matches(3), iff(matches(4), num(5), num(4)), num(3)), num(0));
  const multiplier = call('max', num(1), sum([1, 2, 3].map(col => iff(
    and([cmp('Gt', field('lineCount'), num(col)), cmp('Eq', symbol(col), str('2'))]),
    numeric(at('multipliers', param(`p${col}`))), num(0)))));
  const lookup = bin('Add', bin('Mul', bin('Sub', numeric(target), num(1)), num(3)), bin('Sub', field('lineCount'), num(3)));
  const payout = iff(and([cmp('Gte', field('lineCount'), num(3)), cmp('Gt', numeric(target), num(2))]),
    bin('Mul', numeric(at('linePaytable', lookup)), field('lineFactor')), num(0));
  const nodes = [modify('count', 'Longest matching prefix', 'lineCount'), modify('factor', 'Add participating Wild multipliers', 'lineFactor'),
    modify('payout', 'Look up line payout', 'lineWin'), modify('sum', 'Add line to spin', 'spinCoins'),
    modify('record-win', 'Record line win', 'lineWins'), modify('record-count', 'Record matched length', 'lineCounts'),
    modify('record-symbol', 'Record winning symbol', 'lineSymbols'), modify('record-factor', 'Record line multiplier', 'lineFactors')];
  return { name: 'dog-line', description: 'Five position parameters; longest prefix, table lookup and additive Wild multipliers, expressed with ordinary AST nodes.', nodes, edges: chain(nodes), expressions: {
    count, factor: multiplier, payout, sum: bin('Add', field('spinCoins'), field('lineWin')),
    'record-win': iff(field('traceEnabled'), call('append', field('lineWins'), text(field('lineWin'))), field('lineWins')),
    'record-count': iff(field('traceEnabled'), call('append', field('lineCounts'), text(field('lineCount'))), field('lineCounts')),
    'record-symbol': iff(field('traceEnabled'), call('append', field('lineSymbols'), target), field('lineSymbols')),
    'record-factor': iff(field('traceEnabled'), call('append', field('lineFactors'), text(field('lineFactor'))), field('lineFactors')),
  } };
}

function spinMechanic(free: boolean): Mechanic {
  const prefix = free ? 'free' : 'base'; const reels = free ? model.freeReels : model.baseReels;
  const expressions: Record<string, Expr> = {}; const nodes: Node[] = [];
  for (let col = 0; col < 5; col++) nodes.push({ nodeType: 'draw', id: `stop-${col}`, label: `${prefix} reel ${col + 1} stop`, inputs: ports, outputs: ports,
    stateWriteKey: `stop${col}`, drawWeights: reels[col].map((_, stop) => ({ outcomeId: String(stop), weight: 1, value: 0 })) });
  for (let col = 1; col <= 3; col++) nodes.push({ nodeType: 'draw', id: `wild-${col}`, label: `Wild multiplier on reel ${col + 1}`, inputs: ports, outputs: ports,
    stateWriteKey: `wild${col}`, drawWeights: (free ? model.freeWildMultiplierWeights : model.wildMultiplierWeights).map((weight, i) => ({ outcomeId: String(i + 2), weight, value: 0 })) });
  const cells = Array.from({ length: 15 }, (_, index) => {
    const col = index % 5; const row = Math.floor(index / 5);
    return at(`${prefix}Reel${col}`, bin('Add', numeric(field(`stop${col}`)), num(row)));
  });
  expressions.board = array(cells);
  nodes.push(modify('board', 'Build the 5 × 3 board from stops', 'board'));
  if (free) {
    expressions.overlay = { exprType: 'map', stateKey: 'board', itemName: 'cell', indexName: 'position', itemType: 'String',
      body: iff(cmp('Neq', at('stickyMultipliers', field('position')), str('0')), str('2'), field('cell')) };
    nodes.push(modify('overlay', 'Restore Sticky Wild positions', 'board'));
  }
  const factors = Array.from({ length: 15 }, (_, position) => {
    const col = position % 5;
    const fresh = col === 0 || col === 4 ? str('0') : iff(cmp('Eq', at('board', num(position)), str('2')), field(`wild${col}`), str('0'));
    return free ? iff(cmp('Neq', at('stickyMultipliers', num(position)), str('0')), at('stickyMultipliers', num(position)), fresh) : fresh;
  });
  expressions.multipliers = array(factors); nodes.push(modify('multipliers', 'Apply ×2 / ×3; retain locked values', 'multipliers'));
  if (free) { expressions.sticky = field('multipliers'); nodes.push(modify('sticky', 'Remember Sticky Wild multipliers', 'stickyMultipliers')); }
  expressions.reset = num(0); nodes.push(modify('reset', 'Reset spin payout', 'spinCoins'));
  for (const key of ['lineWins', 'lineCounts', 'lineSymbols', 'lineFactors']) { expressions[`reset-${key}`] = field('emptyArray'); nodes.push(modify(`reset-${key}`, `Reset ${key}`, key)); }
  model.paylines.forEach((line, i) => nodes.push(library(`line-${i + 1}`, 'dog-line', `Payline ${i + 1}`, Object.fromEntries(line.map((row, col) => [`p${col}`, String(row * 5 + col)])))));
  expressions.accumulate = bin('Add', field('totalCoins'), field('spinCoins')); nodes.push(modify('accumulate', 'Add this spin to round payout', 'totalCoins'));
  for (const [key, source] of Object.entries({ boards: 'board', multiplierHistory: 'multipliers', winHistory: 'spinCoins', lineWinHistory: 'lineWins',
    lineCountHistory: 'lineCounts', lineSymbolHistory: 'lineSymbols', lineFactorHistory: 'lineFactors' })) {
    expressions[`snapshot-${key}`] = iff(field('traceEnabled'), call('append', field(key), source === 'spinCoins' ? text(field(source)) : field(source)), field(key));
    nodes.push(modify(`snapshot-${key}`, `Snapshot ${source}`, key));
  }
  return { name: `dog-${prefix}-spin`, description: 'Reel stops, Wild draws, board construction, twenty reusable line subgraphs and trace snapshots. All payout logic is editable graph/AST.', nodes, edges: chain(nodes), expressions };
}

export function createDogHouseGraph(includeFeatureBoundary = true): Record<string, unknown> {
  const base = spinMechanic(false), free = spinMechanic(true), line = lineMechanic();
  const initialState: Record<string, unknown> = { targetRtpPercent: model.targetRtpPercent, calibrationWeightTotal: model.calibrationWeightTotal,
    traceEnabled: false, emptyArray: [], board: [], multipliers: [], stickyMultipliers: Array(15).fill('0'),
    totalCoins: 0, spinCoins: 0, fsCount: 0, bonusGrid: [], boards: [], multiplierHistory: [], winHistory: [],
    lineWins: [], lineCounts: [], lineSymbols: [], lineFactors: [], lineWinHistory: [], lineCountHistory: [], lineSymbolHistory: [], lineFactorHistory: [],
    linePaytable: model.paytable.flatMap(row => [row[2], row[1], row[0]]).map(String) };
  for (const [prefix, reels] of [['base', model.baseReels], ['free', model.freeReels]] as const)
    reels.forEach((reel, col) => { initialState[`${prefix}Reel${col}`] = [...reel, ...reel.slice(0, 2)].map(String); });
  if (includeFeatureBoundary) initialState.bonusCompleted = false;
  const expressions: Record<string, Expr> = {
    trigger: cmp('Gte', { exprType: 'aggregate', func: 'Count', stateKey: 'board', itemName: 'cell', itemType: 'String', predicate: cmp('Eq', field('cell'), str('1')) }, num(3)),
    scatter: bin('Add', field('totalCoins'), num(100)),
    'bonus-count': bin('Add', field('fsCount'), numeric(field('bonusCell'))),
    'bonus-grid': call('append', field('bonusGrid'), field('bonusCell')),
    'fs-stop': cmp('Gte', field('__iter_free-spins__'), field('fsCount')),
    credits: bin('Div', field('totalCoins'), num(20)),
    ...(includeFeatureBoundary ? { 'bonus-completed': { exprType: 'constant', kind: 'Boolean', value: 'true' } as Expr } : {}),
  };
  const nodes: Node[] = [library('base-spin', base.name, 'Base game · 5 reels / 20 lines'),
    { nodeType: 'branch', id: 'bonus-trigger', label: '3 paws trigger Free Spins', conditionId: 'trigger', inputs: ports,
      outputs: { true: { name: 'true', type: 'State' }, false: { name: 'false', type: 'State' } } },
    modify('scatter', 'Scatter pays 5 × stake', 'totalCoins'),
    { nodeType: 'loop', id: 'bonus-grid-loop', label: 'Reveal nine bonus-grid cells', maxIterations: 9, inputs: ports,
      outputs: { body: { name: 'body', type: 'State' }, exit: { name: 'exit', type: 'State' } } },
    { nodeType: 'draw', id: 'bonus-cell', label: 'Award 1 / 2 / 3 free spins', stateWriteKey: 'bonusCell', inputs: ports, outputs: ports,
      drawWeights: model.bonusCellWeights.map((weight, i) => ({ outcomeId: String(i + 1), weight, value: 0 })) },
    modify('bonus-count', 'Sum awarded free spins', 'fsCount'), modify('bonus-grid', 'Record the reveal grid', 'bonusGrid'),
    { nodeType: 'loop', id: 'free-spins', label: '9–27 Free Spins · Sticky Wilds', maxIterations: 27, stopConditionId: 'fs-stop', inputs: ports,
      outputs: { body: { name: 'body', type: 'State' }, exit: { name: 'exit', type: 'State' } } },
    library('free-spin', free.name, 'Free game · separate reels / locked multipliers'),
    ...(includeFeatureBoundary ? [modify('bonus-completed', 'Bonus completed · episode exit', 'bonusCompleted')] : []),
    modify('credits', 'Convert line coins to total-stake credits', 'winCredits'),
    { nodeType: 'metricsSink', id: 'round-metrics', label: 'Round payout & RTP', winStateKey: 'winCredits', winCap: model.roundWinCap, inputs: ports, outputs: {} }];
  const edge = (sourceNodeId: string, sourcePort: string, targetNodeId: string): Edge => ({ id: `${sourceNodeId}-${sourcePort}-${targetNodeId}`, sourceNodeId, sourcePort, targetNodeId, targetPort: 'state' });
  return { schemaVersion: '1.0.0', id: 'dog-house-graph', name: 'The Dog House · UI graph', description: model.provenance,
    initialState, expressions, nodes, edges: [edge('base-spin', 'state', 'bonus-trigger'), edge('bonus-trigger', 'false', 'credits'), edge('bonus-trigger', 'true', 'scatter'),
      edge('scatter', 'state', 'bonus-grid-loop'), edge('bonus-grid-loop', 'body', 'bonus-cell'), edge('bonus-cell', 'state', 'bonus-count'),
      edge('bonus-count', 'state', 'bonus-grid'), edge('bonus-grid-loop', 'exit', 'free-spins'), edge('free-spins', 'body', 'free-spin'),
      ...(includeFeatureBoundary ? [edge('free-spins', 'exit', 'bonus-completed'), edge('bonus-completed', 'state', 'credits')] : [edge('free-spins', 'exit', 'credits')]),
      edge('credits', 'state', 'round-metrics')],
    symbols: model.paytable.map((_, i) => ({ id: String(i + 1), name: ['Bonus', 'Wild', 'Rottweiler', 'Shih Tzu', 'Pug', 'Dachshund', 'Collar', 'Bone', 'A', 'K', 'Q', 'J', '10'][i], kind: i === 0 ? 'Bonus' : i === 1 ? 'Wild' : 'Standard' })),
    paytables: [{ id: 'dog-paytable', entries: model.paytable.slice(2).map((row, i) => ({ symbolId: String(i + 3), counts: [3, 4, 5], payouts: [row[2], row[1], row[0]].map(coins => String(coins / 20)) })) }],
    paylineSets: [{ id: 'dog-lines', paylines: model.paylines.map(positions => ({ positions })) }],
    mechanics: { [base.name]: base, [free.name]: free, [line.name]: line },
  };
}

export { model as dogHouseReference };
