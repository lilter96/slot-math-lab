import { test, expect } from './fixtures';
import { extractSubgraph, type ComposableGraph } from '../src/lib/subgraphComposition';

test('Complete migrated game models open directly in Build and preserve the previous draft', async ({ page }) => {
  const coin = { schemaVersion: '1.0.0', name: 'REF-A Coin', nodes: [
    { nodeType: 'draw', id: 'coin-draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'coin-sink', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'coin-edge', sourceNodeId: 'coin-draw', sourcePort: 'out', targetNodeId: 'coin-sink', targetPort: 'in' }] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(coin))).toString('base64')));
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  for (const name of ['Nine Lives', 'Ancient Dragon', 'Woodland Whisper', 'X7 Club', 'Le Militare']) {
    await library.getByRole('button', { name: `Open ${name} model`, exact: true }).click();
    await expect(page.getByTestId('model-navigation')).toContainText(name);
    await page.getByLabel('Find subgraph').fill('spin');
    await expect(page.getByRole('button', { name: 'Open free-spin graph', exact: true }).or(page.getByRole('button', { name: 'Open hold-and-spin graph', exact: true }))).toBeVisible();
  }
  await library.getByRole('button', { name: 'Restore my model', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="coin-draw"]')).toBeVisible();
});

test('Ready subgraphs are editable, reusable and preserve the previous model', async ({ page }) => {
  const coin = { schemaVersion: '1.0.0', name: 'REF-A Coin', nodes: [
    { nodeType: 'draw', id: 'coin-draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: { out: { name: 'out', type: 'Wins' } } },
    { nodeType: 'metricsSink', id: 'coin-sink', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } },
  ], edges: [{ id: 'coin-edge', sourceNodeId: 'coin-draw', sourcePort: 'out', targetNodeId: 'coin-sink', targetPort: 'in' }] };
  await page.goto('/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(coin))).toString('base64')));
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Inspect Cluster pays subgraph', exact: true }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('cluster-pays');
  await page.getByLabel('Focus graph node').selectOption('candidate-mode');
  await expect(page.locator('.react-flow__node[data-id="candidate-mode"]')).toBeVisible();
  await page.getByRole('button', { name: 'Reuse this subgraph', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('cluster-pays');
  await expect(page.getByRole('button', { name: 'Open full game graph', exact: true })).toBeVisible();
  await page.getByLabel('Find subgraph').fill('cluster-candidates');
  await page.getByRole('button', { name: 'Open cluster-candidates graph', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('candidates');
  await expect(page.locator('.react-flow__node[data-id="candidates"]')).toBeVisible();
  await library.getByRole('button', { name: 'Restore my model', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="coin-draw"]')).toBeVisible();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toHaveCount(0);
  await expect(library.getByRole('button', { name: 'Restore my model', exact: true })).toHaveCount(0);
});

test('Cascade starter exposes shared evaluation, refill and award subgraphs', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cascading clusters example', exact: true }).click();
  for (const name of ['cluster-pays', 'cluster-candidates', 'tumble', 'award-step', 'cascade']) {
    await page.getByLabel('Find subgraph').fill(name);
    await expect(page.getByRole('button', { name: `Open ${name} graph`, exact: true })).toBeVisible();
  }
  await page.getByLabel('Find subgraph').fill('cascade');
  await page.getByRole('button', { name: 'Open cascade graph', exact: true }).click();
  await expect(page.getByLabel('Focus graph node')).toContainText('step');
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await page.getByRole('button', { name: 'Open model data tables', exact: true }).click();
  await expect(page.getByLabel('Data table', { exact: true })).toBeVisible();
});

test('An emptied subgraph remains editable and can return to its parent', async ({ page }) => {
  page.on('dialog', dialog => dialog.accept());
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Inspect Cluster pays subgraph', exact: true }).click();
  const nodes = page.locator('.react-flow__node');
  const picker = page.getByLabel('Focus graph node');
  const ids = await picker.locator('option').evaluateAll(options => options.map(option => (option as HTMLOptionElement).value).filter(Boolean));
  expect(ids.length).toBeGreaterThan(0);
  for (const id of ids) {
    await picker.selectOption(id);
    await page.getByRole('button', { name: 'Delete node', exact: true }).click();
    await expect(picker.locator(`option[value="${id}"]`)).toHaveCount(0);
  }
  await expect(picker.locator('option')).toHaveCount(1);
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
  await page.getByLabel('Find subgraph').fill('cluster-pays');
  await page.getByRole('button', { name: 'Open cluster-pays graph', exact: true }).click();
  await expect(picker.locator('option')).toHaveCount(1);
  await expect(nodes).toHaveCount(0);
  await page.getByRole('button', { name: 'Open full game graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
});

test('Own atoms can be extracted into a definition and reused', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cluster pays example', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('draw');
  await page.getByText('Extract selected atoms', { exact: true }).click();
  await page.getByLabel('Name for extraction').fill('my-board');
  await page.getByRole('button', { name: 'Extract selection', exact: true }).click();
  await page.getByLabel('Find subgraph').fill('my-board');
  await expect(page.getByRole('button', { name: 'Open my-board graph', exact: true })).toBeVisible();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toHaveCount(0);
  await page.getByLabel('Find subgraph').fill('my-board');
  await page.getByRole('button', { name: 'Open my-board graph', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="draw"]')).toBeVisible();
  await page.getByRole('button', { name: 'Reuse this subgraph', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('my-board');
});

test('Mechanics panel uses the same editable definitions as the canvas', async ({ page }) => {
  await page.goto('/build');
  const library = page.getByTestId('starter-library');
  await library.locator('summary').click();
  await library.getByRole('button', { name: 'Open Cluster pays example', exact: true }).click();
  await page.getByRole('button', { name: 'Open mechanic manager', exact: true }).click();
  const manager = page.getByTestId('mechanic-manager');
  await manager.getByLabel('Find a definition').fill('cluster-pays');
  await manager.getByRole('button', { name: 'Edit definition cluster-pays', exact: true }).click();
  await page.getByLabel('Focus graph node').selectOption('candidate-mode');
  await expect(page.locator('.react-flow__node[data-id="candidate-mode"]')).toBeVisible();
  await page.getByRole('button', { name: 'Mechanics', exact: true }).click();
  await manager.getByRole('button', { name: 'Reuse definition cluster-pays', exact: true }).click();
  await expect(page.getByLabel('Subgraph name')).toHaveValue('cluster-pays');
});


test('Extracted transitive expressions and parameterized nested libraries keep the same played payout', async ({ request }) => {
  test.setTimeout(180000);
  const ports = { state: { name: 'state', type: 'State' } };
  const numeric = (value: number) => ({ exprType: 'constant', kind: 'Integer', value: String(value) });
  const ref = (id: string) => ({ exprType: 'fieldAccess', target: 'state', path: ['expressions', id] });
  const amount = { exprType: 'fieldAccess', target: 'state', path: ['amount'] };
  const original: ComposableGraph = {
    schemaVersion: '1.0', name: 'Dependency closure', initialState: { amount: 3 },
    nodes: [
      { id: 'draw', nodeType: 'draw', drawWeights: [{ outcomeId: 'one', weight: 1, value: 1 }], outputs: ports },
      { id: 'add', nodeType: 'modifyState', expressionId: 'award', outputKey: 'amount', inputs: ports, outputs: ports },
      { id: 'nested', nodeType: 'library', mechanicName: 'increment', parameters: { step: '5' }, inputs: ports, outputs: ports },
      { id: 'sink', nodeType: 'metricsSink', winStateKey: 'amount', winCap: 100, inputs: ports },
    ],
    edges: [['draw', 'add'], ['add', 'nested'], ['nested', 'sink']].map(([sourceNodeId, targetNodeId], i) => ({
      id: `e${i}`, sourceNodeId, targetNodeId, sourcePort: 'state', targetPort: 'state',
    })),
    expressions: {
      award: { exprType: 'binary', op: 'Add', left: amount, right: ref('rate') },
      rate: { exprType: 'binary', op: 'Mul', left: ref('factor'), right: ref('base') },
      factor: numeric(2), base: numeric(7),
    },
    mechanics: { increment: { name: 'increment', nodes: [
      { id: 'raise', nodeType: 'modifyState', expressionId: 'raise', outputKey: 'amount', inputs: ports, outputs: ports },
    ], edges: [], expressions: { raise: { exprType: 'binary', op: 'Add', left: amount, right: ref('step') } } } },
  };
  const extracted = extractSubgraph(original, 'payout', ['add', 'nested'], 'own-payout');
  for (const config of [original, extracted]) {
    const data = { config, seed: 42, roundIndex: 0, trace: true };
    let played = await request.post('/api/play/round', { data });
    for (let attempt = 1; played.status() === 429 && attempt < 3; attempt++) {
      const seconds = Number(played.headers()['retry-after'] ?? '1');
      await new Promise(resolve => setTimeout(resolve, Number.isFinite(seconds) ? Math.min(65000, Math.max(1000, seconds * 1000)) : 1000));
      played = await request.post('/api/play/round', { data });
    }
    expect(played.status(), await played.text()).toBe(200);
    const result = await played.json();
    expect(Number(result.win)).toBe(22);
    expect(Number(result.state.amount)).toBe(22);
  }
});
