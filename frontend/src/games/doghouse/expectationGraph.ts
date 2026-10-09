import type { ExpressionAst as E } from '../../lib/expressionParser';
import { createDogHouseGraph } from './graph';
const n = (value: number): E => ({ exprType: 'constant', kind: 'Integer', value: String(value) });
const s = (value: string): E => ({ exprType: 'constant', kind: 'String', value });
const f = (key: string): E => ({ exprType: 'fieldAccess', target: 'state', path: [key] });
const b = (op: string, left: E, right: E): E => ({ exprType: 'binary', op, left, right });
const eq = (left: E, right: E): E => ({ exprType: 'compare', op: 'Eq', left, right });
const choose = (condition: E, thenExpr: E, elseExpr: E): E => ({ exprType: 'if', condition, thenExpr, elseExpr });
const call = (fn: string, ...args: E[]): E => ({ exprType: 'call', function: fn, args });
const add = (values: E[]) => values.reduce((a, x) => b('Add', a, x), n(0));
const mul = (a: E, z: E) => b('Mul', a, z);
const oneMinus = (x: E) => b('Sub', n(1), x);
const ports = { state: { name: 'state', type: 'State' } };
type N = { id: string; nodeType: string; [key: string]: unknown };
function connect(source: string, target: string, port = 'state') { return { id: `${source}-${port}-${target}`, sourceNodeId: source, sourcePort: port, targetNodeId: target, targetPort: 'state' }; }
function modify(id: string, key: string, label: string): N { return { id, nodeType: 'modifyState', label, outputKey: key, expressionId: id, inputs: ports, outputs: ports }; }
function loop(id: string, cap: number, label: string): N { return { id, nodeType: 'loop', label, maxIterations: cap, inputs: ports, outputs: { body: { name: 'body', type: 'State' }, exit: { name: 'exit', type: 'State' } } }; }
function normalize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalize);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).filter(([key, x]) => x != null
    && !['label', 'description', 'name', 'drawWeights'].includes(key)
    && !(key === 'plugins' && Array.isArray(x) && !x.length)).sort(([a], [z]) => a.localeCompare(z)).map(([key, x]) => [key, normalize(x)]));
  return value;
}

/** Authors a second ordinary graph. No expectation is calculated in TypeScript; AST/loops do all arithmetic. */
export function createExpectationGraph(source: Record<string, unknown>): Record<string, unknown> {
  const template = createDogHouseGraph();
  for (const key of ['nodes', 'edges', 'expressions', 'mechanics']) {
    if (JSON.stringify(normalize(source[key])) !== JSON.stringify(normalize(template[key]))) throw new Error('The expectation proof applies to the standard payout/loop expressions. This graph has modified logic; review its proof first.');
  }
  const input = source.initialState as Record<string, unknown>, standard = template.initialState as Record<string, unknown>;
  for (const [key, value] of Object.entries(standard)) if (!/^(baseReel|freeReel|linePaytable|traceEnabled|targetRtpPercent)$|^(baseReel|freeReel)\d+$/.test(key) && JSON.stringify(input[key]) !== JSON.stringify(value)) throw new Error(`Reference requires the standard initial ${key}.`);
  if (!Number.isSafeInteger(input.targetRtpPercent) || Number(input.targetRtpPercent) <= 0) throw new Error('Target RTP must be a positive integer percentage in project data.');
  const mechanics = source.mechanics as Record<string, { nodes: N[] }>;
  const initial: Record<string, unknown> = { ...input, isFree: false, expectedSpinCoins: 0, cumulativeBonus: 0, conditionalBonus: 0, zero: 0 };
  const expressions: Record<string, E> = {};
  const root: N[] = [], rootEdges: ReturnType<typeof connect>[] = [];
  const push = (id: string, key: string, label: string, expr: E) => { if (root.length) rootEdges.push(connect(root.at(-1)!.id, id)); root.push(modify(id, key, label)); expressions[id] = expr; };
  const count = (key: string, target: E): E => ({ exprType: 'aggregate', func: 'Count', stateKey: key, itemName: 'symbol', itemType: 'String', predicate: eq(f('symbol'), target) });
  const probability = (key: string, target: E) => b('Div', count(key, target), call('length', f(key)));
  if (!Array.isArray(input.linePaytable) || input.linePaytable.length !== 39 || input.linePaytable.some(x => !Number.isSafeInteger(Number(x)) || Number(x) < 0)) throw new Error('Reference requires 39 nonnegative integer coin payouts.');
  const maxPay = Math.max(...input.linePaytable.map(Number));
  if (Number((source.nodes as N[]).find(x => x.nodeType === 'metricsSink')?.winCap) < 28 * maxPay * 9 + 5) throw new Error('A binding round cap invalidates the linear expectation proof.');
  for (const prefix of ['base', 'free']) for (let col = 0; col < 5; col++) {
    const draw = mechanics[`dog-${prefix}-spin`].nodes.find(x => x.id === `stop-${col}`)!;
    const weights = draw.drawWeights as { outcomeId: string; weight: number; value: number }[];
    const strip = input[`${prefix}Reel${col}`] as string[];
    if (!weights.length || !weights.every((x, i) => x.weight > 0 && x.weight === weights[0].weight && x.value === 0 && x.outcomeId === String(i))) throw new Error('This expectation reference requires uniform circular reel stops.');
    if (!Array.isArray(strip) || strip.length !== weights.length + 2 || strip[0] !== strip.at(-2) || strip[1] !== strip.at(-1) || strip.some(x => !/^(?:[1-9]|1[0-3])$/.test(x))) throw new Error('Reel data must include two wrapped cells and symbol IDs 1–13.');
    const reel = strip.slice(0, -2);
    if ((col === 0 || col === 4) && reel.includes('2')) throw new Error('The reference requires Wilds only on reels 2–4.');
    if ((prefix === 'free' || col === 1 || col === 3) && reel.includes('1')) throw new Error('Bonus symbols must occur only on base reels 1, 3 and 5.');
    if (reel.some((_, stop) => [0, 1, 2].filter(row => reel[(stop + row) % reel.length] === '1').length > 1)) throw new Error('The reference requires at most one bonus symbol per visible reel window.');
    initial[`reference-${prefix}-${col}`] = reel;
    initial[`survival-${col}`] = 1;
    push(`wild-prob-${prefix}-${col}`, `wild-${prefix}-${col}`, `Natural Wild probability · ${prefix} reel ${col + 1}`, probability(`reference-${prefix}-${col}`, s('2')));
  }
  for (let col = 1; col <= 3; col++) {
    const extract = (prefix: string) => {
      const w = mechanics[`dog-${prefix}-spin`].nodes.find(x => x.id === `wild-${col}`)!.drawWeights as { outcomeId: string; weight: number; value: number }[];
      if (w.length !== 2 || w.some((x, i) => x.outcomeId !== String(i + 2) || !Number.isSafeInteger(x.weight) || x.weight < 0 || x.value !== 0) || w.every(x => x.weight === 0)) throw new Error('Reference requires independent ×2 / ×3 multiplier draws.');
      return b('Div', add(w.map(x => mul(n(Number(x.outcomeId)), n(x.weight)))), n(w.reduce((a, x) => a + x.weight, 0)));
    };
    push(`mean-base-${col}`, `mean-base-${col}`, `Mean fresh base multiplier · reel ${col + 1}`, extract('base'));
    push(`mean-free-${col}`, `mean-free-${col}`, `Mean fresh free multiplier · reel ${col + 1}`, extract('free'));
  }
  const bonus = (source.nodes as N[]).find(x => x.id === 'bonus-cell')!.drawWeights as { outcomeId: string; weight: number; value: number }[];
  if (bonus.length !== 3 || bonus.some((x, i) => x.outcomeId !== String(i + 1) || !Number.isSafeInteger(x.weight) || x.weight < 0 || x.value !== 0) || bonus.every(x => x.weight === 0)) throw new Error('Reference requires independent bonus cells awarding 1 / 2 / 3 spins.');
  const bonusTotal = bonus.reduce((a, x) => a + x.weight, 0);
  for (let count = 0; count <= 27; count++) initial[`bonus-pmf-${count}`] = count === 0 ? 1 : 0;
  const pmfExpressions: Record<string, E> = {}, pmfNodes: N[] = [];
  for (let count = 27; count >= 0; count--) {
    const id = `pmf-${count}`; pmfNodes.push(modify(id, `bonus-pmf-${count}`, `Convolve cell into P(N=${count})`));
    pmfExpressions[id] = add(bonus.flatMap((x, i) => count - i - 1 >= 0 ? [mul(f(`bonus-pmf-${count - i - 1}`), b('Div', n(x.weight), n(bonusTotal)))] : []));
  }
  const pmf = { name: 'reference-bonus-pmf', description: 'Nine bounded convolutions; descending updates preserve the previous cell distribution.', nodes: pmfNodes, edges: pmfNodes.slice(1).map((x, i) => connect(pmfNodes[i].id, x.id)), expressions: pmfExpressions };
  const spinExpressions: Record<string, E> = {}, spinNodes: N[] = [modify('reset-ev', 'expectedSpinCoins', 'Reset line expectation'), loop('symbols', 11, 'Sum symbols 3–13')];
  const body: N[] = [];
  const step = (id: string, key: string, label: string, expr: E) => { body.push(modify(id, key, label)); spinExpressions[id] = expr; };
  spinExpressions['reset-ev'] = n(0);
  step('target', 'targetSymbol', 'Next regular symbol', call('toString', b('Add', f('__iter_symbols__'), n(3))));
  for (let col = 0; col < 5; col++) {
    step(`ps-${col}`, `ps-${col}`, `Ordinary-symbol marginal · reel ${col + 1}`, choose(f('isFree'), mul(f(`survival-${col}`), probability(`reference-free-${col}`, f('targetSymbol'))), probability(`reference-base-${col}`, f('targetSymbol'))));
    step(`pw-${col}`, `pw-${col}`, `Sticky Wild marginal · reel ${col + 1}`, choose(f('isFree'), oneMinus(mul(f(`survival-${col}`), oneMinus(f(`wild-free-${col}`)))), f(`wild-base-${col}`)));
  }
  step('match-start', 'matchMass', 'First reel must contain the target', f('ps-0'));
  step('ordinary-start', 'ordinaryMass', 'No-Wild matching probability', f('ps-0'));
  step('multiplier-start', 'multiplierMass', 'Weighted additive multiplier expectation', n(0));
  for (let col = 1; col < 5; col++) {
    const match = b('Add', f(`ps-${col}`), f(`pw-${col}`));
    const mean = col === 4 ? n(0) : choose(f('isFree'), f(`mean-free-${col}`), f(`mean-base-${col}`));
    step(`mult-${col}`, 'multiplierMass', `Extend multiplier mass through reel ${col + 1}`, b('Add', mul(f('multiplierMass'), match), mul(mul(f('matchMass'), f(`pw-${col}`)), mean)));
    step(`ordinary-${col}`, 'ordinaryMass', `Extend no-Wild mass through reel ${col + 1}`, mul(f('ordinaryMass'), f(`ps-${col}`)));
    step(`match-${col}`, 'matchMass', `Extend matching mass through reel ${col + 1}`, mul(f('matchMass'), match));
    if (col >= 2) {
      const lookup = b('Add', mul(b('Sub', call('toNumber', f('targetSymbol')), n(1)), n(3)), n(col - 2));
      const pays = call('toNumber', call('index', f('linePaytable'), lookup));
      const ends = col === 4 ? n(1) : oneMinus(b('Add', f(`ps-${col + 1}`), f(`pw-${col + 1}`)));
      step(`pay-${col + 1}`, 'expectedSpinCoins', `Longest ${col + 1}-symbol prefix expectation`, b('Add', f('expectedSpinCoins'), mul(mul(pays, b('Add', f('multiplierMass'), f('ordinaryMass'))), ends)));
    }
  }
  // Uniform circular stops give identical row marginals. Twenty line expectations / twenty stake coins = one line's coin EV.
  const spin = { name: 'reference-spin-ev', description: 'Linearity over 20 lines. Independent reel marginals; additive Wild multiplier mass plus the no-Wild mass. Rational arithmetic only.',
    nodes: [...spinNodes, modify('done', 'expectedSpinCoins', 'Return the expected line payout'), ...body], expressions: { ...spinExpressions, done: f('expectedSpinCoins') },
    edges: [connect('reset-ev', 'symbols'), connect('symbols', body[0].id, 'body'), connect('symbols', 'done', 'exit'), ...body.slice(1).map((x, i) => connect(body[i].id, x.id))] };
  const baseId = 'base-ev'; rootEdges.push(connect(root.at(-1)!.id, baseId)); root.push({ id: baseId, nodeType: 'library', label: 'Exact base payline expectation', mechanicName: spin.name, inputs: ports, outputs: ports });
  push('save-base', 'baseRtp', 'Save base contribution', f('expectedSpinCoins'));
  push('trigger', 'triggerProbability', 'Three independent paw windows', [0, 2, 4].map(col => mul(n(3), probability(`reference-base-${col}`, s('1')))).reduce(mul));
  push('scatter', 'scatterRtp', 'Five total bets per trigger', mul(n(5), f('triggerProbability')));
  rootEdges.push(connect(root.at(-1)!.id, 'bonus-pmf-loop')); root.push(loop('bonus-pmf-loop', 9, 'Nine bonus-grid cell convolutions'));
  root.push({ id: 'bonus-pmf-body', nodeType: 'library', label: 'Bonus-count distribution', mechanicName: pmf.name, inputs: ports, outputs: ports });
  rootEdges.push(connect('bonus-pmf-loop', 'bonus-pmf-body', 'body'));
  root.push(modify('enable-free', 'isFree', 'Switch to Sticky Wild recurrence')); expressions['enable-free'] = { exprType: 'constant', kind: 'Boolean', value: 'true' };
  rootEdges.push(connect('bonus-pmf-loop', 'enable-free', 'exit'));
  const resetNodes = [modify('reset-cumulative', 'cumulativeBonus', 'Reset cumulative bonus expectation'), modify('reset-conditional', 'conditionalBonus', 'Reset conditional bonus expectation'),
    ...Array.from({ length: 5 }, (_, col) => modify(`reset-survival-${col}`, `survival-${col}`, `Start reel ${col + 1} unlocked`)), loop('turns', 27, 'Free-spin expectations for N=1…27')];
  const bonusExpressions: Record<string, E> = { 'reset-cumulative': n(0), 'reset-conditional': n(0), done: f('conditionalBonus') };
  for (let col = 0; col < 5; col++) bonusExpressions[`reset-survival-${col}`] = n(1);
  const freeNodes: N[] = [{ id: 'free-ev', nodeType: 'library', label: 'Exact sticky-cell line expectation', mechanicName: spin.name, inputs: ports, outputs: ports }, modify('cumulative', 'cumulativeBonus', 'Cumulative expectation through spin N'), modify('conditional', 'conditionalBonus', 'Weight cumulative expectation by P(N)')];
  bonusExpressions.cumulative = b('Add', f('cumulativeBonus'), f('expectedSpinCoins'));
  const pmfAtN = Array.from({ length: 27 }, (_, i) => i + 1).reverse().reduce((rest, count) => choose(eq(f('__iter_turns__'), n(count - 1)), f(`bonus-pmf-${count}`), rest), n(0));
  bonusExpressions.conditional = b('Add', f('conditionalBonus'), mul(pmfAtN, f('cumulativeBonus')));
  for (let col = 0; col < 5; col++) { const id = `survival-next-${col}`; freeNodes.push(modify(id, `survival-${col}`, `P(cell still unlocked) after this spin · reel ${col + 1}`)); bonusExpressions[id] = mul(f(`survival-${col}`), oneMinus(f(`wild-free-${col}`))); }
  const bonusEv = { name: 'reference-bonus-ev', description: 'Exact conditional bonus expectation, starting with no locked positions. Repeatable for current, all-×2 and all-×3 weights.',
    nodes: [...resetNodes, modify('done', 'conditionalBonus', 'Return conditional bonus expectation'), ...freeNodes], expressions: bonusExpressions,
    edges: [...resetNodes.slice(1).map((x, i) => connect(resetNodes[i].id, x.id)), connect('turns', 'done', 'exit'), connect('turns', 'free-ev', 'body'), ...freeNodes.slice(1).map((x, i) => connect(freeNodes[i].id, x.id))] };
  const runBonus = (id: string, label: string) => { rootEdges.push(connect(root.at(-1)!.id, id)); root.push({ id, nodeType: 'library', label, mechanicName: bonusEv.name, inputs: ports, outputs: ports }); };
  runBonus('current-bonus-ev', 'Bonus expectation at current graph weights');
  push('current-conditional', 'currentConditionalBonus', 'Remember current conditional bonus expectation', f('conditionalBonus'));
  push('bonus-total', 'bonusRtp', 'Exact bonus contribution', mul(f('triggerProbability'), f('conditionalBonus')));
  push('rtp-total', 'expectedRtp', 'Exact paid-round RTP', add([f('baseRtp'), f('scatterRtp'), f('bonusRtp')]));
  push('target-rtp', 'targetRtp', 'Target percentage / 100', b('Div', f('targetRtpPercent'), n(100)));
  push('target-delta', 'targetDelta', 'Actual expectation minus target', b('Sub', f('expectedRtp'), f('targetRtp')));
  const magnitude = choose({ exprType: 'compare', op: 'Lt', left: f('targetDelta'), right: n(0) }, b('Sub', n(0), f('targetDelta')), f('targetDelta'));
  push('target-met', 'targetMet', 'Within 10⁻¹⁰ percentage points of target', { exprType: 'compare', op: 'Lte', left: magnitude, right: b('Div', n(1), n(1000000000000)) });
  for (const factor of [2, 3]) {
    for (let col = 1; col <= 3; col++) push(`calibration-${factor}-${col}`, `mean-free-${col}`, `Calibrate with all fresh Wilds ×${factor}`, n(factor));
    runBonus(`calibration-bonus-${factor}`, `Bonus expectation with ×${factor} Wilds`);
    push(`calibration-rtp-${factor}`, `calibrationRtp${factor}`, `Paid-round RTP at ×${factor}`, add([f('baseRtp'), f('scatterRtp'), mul(f('triggerProbability'), f('conditionalBonus'))]));
  }
  const range = b('Sub', f('calibrationRtp3'), f('calibrationRtp2'));
  const positiveRange = { exprType: 'compare', op: 'Gt', left: range, right: n(0) };
  push('calibration-probability', 'requiredP3', 'Solve P(×3) = (target − RTP₂) / (RTP₃ − RTP₂)', choose(positiveRange, b('Div', b('Sub', f('targetRtp'), f('calibrationRtp2')), range), n(0)));
  push('calibration-feasible', 'calibrationFeasible', 'Target lies within the editable multiplier range', b('And', positiveRange, b('And', { exprType: 'compare', op: 'Gte', left: f('requiredP3'), right: n(0) }, { exprType: 'compare', op: 'Lte', left: f('requiredP3'), right: n(1) })));
  push('calibration-weight-3', 'calibratedWeight3', 'Round P(×3) × integer weight budget', call('round', mul(f('requiredP3'), f('calibrationWeightTotal'))));
  push('calibration-weight-2', 'calibratedWeight2', 'Remaining integer weight for ×2', b('Sub', f('calibrationWeightTotal'), f('calibratedWeight3')));
  push('restore-conditional', 'conditionalBonus', 'Restore current graph conditional expectation', f('currentConditionalBonus'));
  root.push({ id: 'reference-sink', nodeType: 'metricsSink', label: 'Inspect expectedRtp and targetMet in final state · not a payout distribution', winStateKey: 'zero', winCap: 1, inputs: ports, outputs: {} }); rootEdges.push(connect(root.at(-2)!.id, 'reference-sink'));
  return { schemaVersion: '1.0.0', name: 'Dog House · rational expectation proof', uiAnalysisKind: 'expectationProof', initialState: initial, expressions, nodes: root, edges: rootEdges,
    mechanics: { [spin.name]: spin, [pmf.name]: pmf, [bonusEv.name]: bonusEv } };
}
