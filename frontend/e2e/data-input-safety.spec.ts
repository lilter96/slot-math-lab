import { createHash } from 'node:crypto';
import { test, expect, type Page } from './fixtures';

const input = (id: string) => ({ kind: 'specification', id, version: 'v1', sha256: 'b'.repeat(64) });
const config = { schemaVersion: '1.0.0', name: 'Data safety fixture', nodes: [{ id: 'sink', nodeType: 'metricsSink', winCap: 10 }], edges: [],
  initialState: { weights: [2, 4, 8], counter: 7 }, expressions: { keep: { exprType: 'constant', kind: 'Integer', value: '42' } }, evidenceInputs: [input('first'), input('second'), input('third')] };
const link = '/build?load=' + encodeURIComponent(Buffer.from(encodeURIComponent(JSON.stringify(config))).toString('base64'));
const digest = (text: string | Buffer) => createHash('sha256').update(text).digest('hex');
async function openData(page: Page) {
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click();
  return page.getByRole('dialog', { name: 'Project data & expressions' });
}
async function openEvidence(page: Page) {
  const dialog = await openData(page);
  await dialog.getByRole('button', { name: 'Rule / spec / asset inputs', exact: true }).click();
  return dialog.getByRole('region', { name: 'External evidence inputs' });
}
async function delayHash(page: Page) {
  await page.addInitScript(() => {
    const host = window as unknown as { hashStarted: boolean; hashFinished: boolean; releaseHash(): void };
    const original = crypto.subtle.digest.bind(crypto.subtle);
    const gate = new Promise<void>(resolve => { host.releaseHash = resolve; });
    crypto.subtle.digest = async (algorithm, bytes) => {
      const result = await original(algorithm, bytes);
      if (new TextDecoder().decode(bytes as ArrayBuffer) === 'late-evidence') {
        host.hashStarted = true;
        await gate;
        host.hashFinished = true;
      }
      return result;
    };
  });
}
async function releaseHash(page: Page) {
  await page.evaluate(() => (window as unknown as { releaseHash(): void }).releaseHash());
  await expect.poll(() => page.evaluate(() => (window as unknown as { hashFinished: boolean }).hashFinished)).toBe(true);
}

test('Raw row deletion requires confirmation and Add cannot overwrite existing data or expressions', async ({ page }) => {
  await page.goto(link);
  const dialog = await openData(page);
  await dialog.getByLabel('Data table', { exact: true }).selectOption('weights');
  page.once('dialog', async prompt => { expect(prompt.message()).toContain('shift index'); await prompt.dismiss(); });
  await dialog.getByRole('button', { name: 'Delete data row 1', exact: true }).click();
  await expect(dialog.getByLabel('Data row 1', { exact: true })).toHaveValue('2');
  await expect(dialog.getByLabel('Data row 3', { exact: true })).toHaveValue('8');
  page.once('dialog', prompt => prompt.accept());
  await dialog.getByRole('button', { name: 'Delete data row 1', exact: true }).click();
  await expect(dialog.getByLabel('Data row 1', { exact: true })).toHaveValue('4');
  await expect(dialog.getByLabel('Data row 2', { exact: true })).toHaveValue('8');
  await dialog.getByLabel('New data field').fill('weights');
  await expect(dialog.getByRole('button', { name: 'Add array field', exact: true })).toBeDisabled();
  await expect(dialog.getByRole('alert')).toContainText('already exists');
  await dialog.getByRole('button', { name: 'Expressions', exact: true }).click();
  await dialog.getByLabel('New expression ID').fill('keep');
  await expect(dialog.getByRole('button', { name: 'Add expression', exact: true })).toBeDisabled();
  await expect(dialog.getByLabel('Expression Value', { exact: true })).toHaveValue('42');
});

test('Evidence files retain actual SHA-256 and removal preserves inputs on cancel', async ({ page }) => {
  await page.goto(link);
  const evidence = await openEvidence(page), first = evidence.locator('fieldset').first();
  await first.getByLabel('Hash and embed file · up to 64 KiB').setInputFiles({ name: 'rule.txt', mimeType: 'text/plain', buffer: Buffer.from('actual rule') });
  await expect(first.getByLabel('SHA-256', { exact: true })).toHaveValue(digest('actual rule'));
  await expect(first).toContainText('Embedded content');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export project', exact: true }).click();
  const { readFile } = await import('node:fs/promises');
  const saved = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
  expect(Buffer.from(saved.evidenceInputs[0].contentBase64, 'base64').toString()).toBe('actual rule');
  expect(saved.evidenceInputs[0]).toMatchObject({ ...input('first'), sha256: digest('actual rule') });
  expect(saved.evidenceInputs.slice(1)).toEqual(config.evidenceInputs.slice(1));
  expect(saved.initialState).toEqual(config.initialState);
  await openEvidence(page);
  page.once('dialog', prompt => prompt.dismiss());
  await first.getByRole('button', { name: 'Remove evidence input first', exact: true }).click();
  await expect(evidence.locator('fieldset')).toHaveCount(3);
  page.once('dialog', prompt => prompt.accept());
  await first.getByRole('button', { name: 'Remove evidence input first', exact: true }).click();
  await expect(evidence.locator('fieldset')).toHaveCount(2);
  await expect(evidence.locator('fieldset').first().getByLabel('ID', { exact: true })).toHaveValue('second');
  await expect(evidence.locator('fieldset').first().getByLabel('SHA-256', { exact: true })).toHaveValue('b'.repeat(64));
});

test('Late hashing cannot attach a deleted input file to the next row', async ({ page }) => {
  await delayHash(page); await page.goto(link);
  const evidence = await openEvidence(page);
  await evidence.locator('fieldset').first().getByLabel('Hash and embed file · up to 64 KiB').setInputFiles({ name: 'late.txt', mimeType: 'text/plain', buffer: Buffer.from('late-evidence') });
  await expect.poll(() => page.evaluate(() => (window as unknown as { hashStarted: boolean }).hashStarted)).toBe(true);
  page.once('dialog', prompt => prompt.accept());
  await evidence.getByRole('button', { name: 'Remove evidence input first', exact: true }).click();
  await releaseHash(page);
  await expect(evidence.locator('fieldset').first().getByLabel('ID', { exact: true })).toHaveValue('second');
  await expect(evidence.locator('fieldset').first().getByLabel('SHA-256', { exact: true })).toHaveValue('b'.repeat(64));
  await expect(evidence).not.toContainText('Embedded content');
});

test('Late hashing cannot modify another project after the evidence editor closes', async ({ page }) => {
  await delayHash(page); await page.goto(link);
  const evidence = await openEvidence(page);
  await evidence.locator('fieldset').first().getByLabel('Hash and embed file · up to 64 KiB').setInputFiles({ name: 'late.txt', mimeType: 'text/plain', buffer: Buffer.from('late-evidence') });
  await expect.poll(() => page.evaluate(() => (window as unknown as { hashStarted: boolean }).hashStarted)).toBe(true);
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await page.getByLabel('Import project file', { exact: true }).setInputFiles({ name: 'other.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({ ...config, name: 'Other private project', evidenceInputs: [input('other')] })) });
  const other = await openEvidence(page);
  await releaseHash(page);
  await expect(other.locator('fieldset')).toHaveCount(1);
  await expect(other.getByLabel('ID', { exact: true })).toHaveValue('other');
  await expect(other.getByLabel('SHA-256', { exact: true })).toHaveValue('b'.repeat(64));
  await expect(other).not.toContainText('Embedded content');
});

test('A newer file supersedes pending hashing for the same input', async ({ page }) => {
  await delayHash(page); await page.goto(link);
  const evidence = await openEvidence(page), first = evidence.locator('fieldset').first(), file = first.getByLabel('Hash and embed file · up to 64 KiB');
  await file.setInputFiles({ name: 'old.txt', mimeType: 'text/plain', buffer: Buffer.from('late-evidence') });
  await expect.poll(() => page.evaluate(() => (window as unknown as { hashStarted: boolean }).hashStarted)).toBe(true);
  await file.setInputFiles({ name: 'new.txt', mimeType: 'text/plain', buffer: Buffer.from('new rule') });
  await expect(first.getByLabel('SHA-256', { exact: true })).toHaveValue(digest('new rule'));
  await releaseHash(page);
  await expect(first.getByLabel('SHA-256', { exact: true })).toHaveValue(digest('new rule'));
});

test('Evidence byte limits reject oversized and excess aggregate content without replacing identities', async ({ page }) => {
  await page.goto(link);
  const evidence = await openEvidence(page), rows = evidence.locator('fieldset'), bytes = Buffer.alloc(65536, 65);
  for (const index of [0, 1]) {
    await rows.nth(index).getByLabel('Hash and embed file · up to 64 KiB').setInputFiles({ name: `limit-${index}.bin`, mimeType: 'application/octet-stream', buffer: bytes });
    await expect(rows.nth(index).getByLabel('SHA-256', { exact: true })).toHaveValue(digest(bytes));
  }
  const third = rows.nth(2).getByLabel('Hash and embed file · up to 64 KiB');
  await third.setInputFiles({ name: 'excess.bin', mimeType: 'application/octet-stream', buffer: Buffer.from('x') });
  await expect(evidence.getByRole('alert')).toContainText('128 KiB in total');
  await expect(rows.nth(2).getByLabel('SHA-256', { exact: true })).toHaveValue('b'.repeat(64));
  await third.setInputFiles({ name: 'too-large.bin', mimeType: 'application/octet-stream', buffer: Buffer.alloc(65537) });
  await expect(evidence.getByRole('alert')).toContainText('64 KiB per file');
  await expect(rows.nth(2)).not.toContainText('Embedded content');
});

test('Plugin registry exposes real capabilities without pretending to register, remove or prove a method', async ({ page }) => {
  await page.route('**/api/features', route => route.fulfill({ json: { ai: false, autoTune: false, plugins: true, play: false } }));
  await page.route('**/api/plugins', route => route.fulfill({ json: [{ pluginId: 'provisioned', contract: 'IEvaluator', version: '2.0', isConformant: true }] }));
  const mutations: string[] = [];
  page.on('request', request => { if (request.url().includes('/api/plugins') && request.method() !== 'GET') mutations.push(request.method()); });
  await page.goto(link);
  await page.getByRole('button', { name: 'Open mechanic manager', exact: true }).click();
  await page.getByRole('button', { name: 'Plugins', exact: true }).click();
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  await inspector.getByRole('button', { name: 'Inspect plugin provisioned', exact: true }).click();
  await expect(inspector.getByRole('button', { name: 'Remove plugin provisioned', exact: true })).toBeDisabled();
  await expect(inspector.getByRole('button', { name: 'Register', exact: true })).toHaveCount(0);
  await expect(inspector).toContainText('evaluation-method compatibility');
  await expect(inspector.locator('.prov.exact, .prov.sampled')).toHaveCount(0);
  expect(mutations).toEqual([]);
});

test('An empty-body plugin failure is an actionable error and retry loads the registry', async ({ page }) => {
  await page.route('**/api/features', route => route.fulfill({ json: { ai: false, autoTune: false, plugins: true, play: false } }));
  let available = false;
  await page.route('**/api/plugins', route => available ? route.fulfill({ json: [] }) : route.fulfill({ status: 503, body: '' }));
  await page.goto(link);
  await page.getByRole('button', { name: 'Open mechanic manager', exact: true }).click();
  await page.getByRole('button', { name: 'Plugins', exact: true }).click();
  const inspector = page.getByRole('complementary', { name: 'Context inspector' });
  await expect(inspector.getByRole('alert')).toContainText('HTTP 503');
  await expect(inspector).not.toContainText('No server plugins');
  available = true;
  await inspector.getByRole('button', { name: 'Retry plugin registry', exact: true }).click();
  await expect(inspector).toContainText('No server plugins are registered');
  await expect(inspector.getByRole('alert')).toHaveCount(0);
});
