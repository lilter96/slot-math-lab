import { chromium } from '../../frontend/node_modules/playwright/index.mjs';
import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { waitForDeployment } from './requests.mjs';
const base = process.env.MEASUREMENTS_BASE_URL;
if (!base) throw new Error('Set MEASUREMENTS_BASE_URL.');
const browser = await chromium.launch();
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1600, height: 1100 } });
const page = await context.newPage(), errors = [], frames = [];
page.on('pageerror', e => errors.push(e.message));
page.on('websocket', ws => ws.on('framereceived', e => { for (const part of e.payload.toString().split('\x1e')) { try { const m = JSON.parse(part); if (m.target === 'ProgressUpdate') frames.push(m.arguments[0]); } catch { /* Control frame */ } } }));
async function get(path) { const r = await context.request.get(base + path); if (!r.ok()) throw new Error(`Read HTTP ${r.status()}`); return r.json(); }
async function post(path, data, status = 200) { const r = await context.request.post(base + path, { data }); if (r.status() !== status) throw new Error(`Mutation HTTP ${r.status()}: ${await r.text()}`); return r.json(); }
try {
  await waitForDeployment(context.request, base);
  await page.goto(base + '/build'); await page.getByLabel('Username').fill('operator');
  await page.getByLabel('Password').fill((await readFile('production-secrets/operator-password.txt', 'utf8')).trim());
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
  await page.goto(base + '/build'); await page.getByLabel('Import project file').setInputFiles('frontend/e2e/fixtures/record-model.json');
  await page.getByLabel('Focus graph node').selectOption('sink'); await page.getByLabel('Round total award before WinCap').check();
  await page.getByRole('button', { name: 'Edit project data and expressions', exact: true }).click(); const dialog = page.getByRole('dialog', { name: 'Project data & expressions' });
  await dialog.getByRole('button', { name: 'Rule / spec / asset inputs', exact: true }).click(); await dialog.getByRole('button', { name: 'Add evidence input', exact: true }).click();
  await dialog.getByLabel('ID', { exact: true }).fill('production-record-rule'); await dialog.getByLabel('Version', { exact: true }).fill('1');
  const bytes = Buffer.from('Record award rounds once before cap.');
  await dialog.getByLabel('Hash and embed file · up to 64 KiB').setInputFiles({ name: 'rule.txt', mimeType: 'text/plain', buffer: bytes });
  await expect(dialog.getByLabel('SHA-256', { exact: true })).toHaveValue(createHash('sha256').update(bytes).digest('hex'));
  await dialog.getByRole('button', { name: 'Close dialog' }).click(); await page.getByRole('tab', { name: 'Simulate', exact: true }).click(); await page.getByLabel('Simulation spins').fill('1000000');
  const launch = page.waitForResponse(r => new URL(r.url()).pathname === '/api/runs' && r.request().method() === 'POST' && r.status() !== 429);
  await page.getByRole('button', { name: /^▶ Start (new )?run$/ }).click(); const accepted = await launch; expect(accepted.status()).toBe(202); const run = await accepted.json();
  await expect(page.locator('.run-status')).toHaveText('completed', { timeout: 70000 }); await page.goto(base + `/results?run=${run.id}`);
  const workbench = page.getByRole('region', { name: 'Verification experiments', exact: true }); await expect(workbench).toBeVisible();
  await workbench.getByRole('tab', { name: 'Rule / spec / asset evidence' }).click(); await expect(workbench.getByText('specification / production-record-rule')).toBeVisible();
  const resource = await post(`/api/runs/measurements/resource-impact?runId=${run.id}`, { reference: { transientMatrix: [['1/2']], rewards: ['2'] }, horizon: 3 });
  expect(resource.report.stopProbability).toBe('1/8'); expect(resource.report.omittedReward).toBe('1/2');
  const sampling = await post(`/api/runs/measurements/sampling-design?runId=${run.id}`, { atoms: [{ id: 'loss', value: '0', targetProbability: '3/4', proposalProbability: '1/2', stratum: 'all' }, { id: 'win', value: '4', targetProbability: '1/4', proposalProbability: '1/2', stratum: 'all' }], mode: 'importance', samples: 100, seed: 42 });
  expect(sampling.report.exactEstimatorVariance).toBe('1/100');
  const snapshot = await get(`/api/runs/${run.id}`);
  const experiment = await post('/api/runs/experiments', { configId: snapshot.configId, configVersion: snapshot.configVersion, variants: [{ name: 'Cap one', kind: 'winCap', key: 'winCap', value: 1, nodeId: 'sink' }], measurements: [], samples: 1000, seed: 42 }, 202);
  let report; await expect.poll(async () => { report = await get(`/api/runs/experiments/${experiment.id}`); return report.experiment.status; }, { timeout: 70000 }).toBe('completed');
  expect(report.runs.every(r => r.status === 'completed')).toBe(true);
  const evidence = await get(`/api/runs/${run.id}/evidence`); expect(evidence.inputVerified).toBe(true); expect(evidence.diagnostics.map(d => d.kind)).toEqual(expect.arrayContaining(['resource-impact', 'sampling-design']));
  await page.reload(); await expect(page.getByRole('region', { name: 'Verification experiments' })).toBeVisible(); await page.screenshot({ path: 'docs/verification/native-experiments-production.png', fullPage: true });
  expect(errors).toEqual([]); expect(frames.length).toBeGreaterThan(0);
  await writeFile('docs/verification/native-experiments-production.json', JSON.stringify({ checkedAt: new Date().toISOString(), runId: run.id, experimentId: experiment.id, manifestSha256: experiment.manifestSha256, runtime: snapshot.runtimeProvenance, resource, sampling, report, inputVerified: evidence.inputVerified, websocketFrames: frames.length, browserErrors: errors }, null, 2) + '\n');
  console.log(JSON.stringify({ runId: run.id, experimentId: experiment.id, websocketFrames: frames.length, browserErrors: errors.length }));
} finally { await context.close(); await browser.close(); }
