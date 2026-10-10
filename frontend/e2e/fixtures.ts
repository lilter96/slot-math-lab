import { test as base, expect, type APIRequestContext, type APIResponse, type Page, type Response as BrowserResponse } from '@playwright/test';
export { expect };
export type { APIRequestContext, APIResponse, Page, WebSocketRoute } from '@playwright/test';

/** Wait through explicit quota rejection, then require an accepted launch.
 * Other errors fail immediately, before attempting to parse them as run JSON. */
export function waitForRunLaunch(page: Page): Promise<BrowserResponse> {
  test.setTimeout(Math.max(test.info().timeout, 180000));
  return page.waitForResponse(response => new URL(response.url()).pathname === '/api/runs'
    && response.request().method() === 'POST' && response.status() !== 429, { timeout: 70000 })
    .then(response => { expect(response.status(), 'Run launch was accepted').toBe(202); return response; });
}

/** Await the accepted calculation before asserting its rendered mathematics.
 * Empty quota rejections are transport state, never a successful JSON result. */
export async function calculateWithQuota(page: Page, path: string, buttonName: string): Promise<void> {
  test.setTimeout(Math.max(test.info().timeout, 180000));
  const accepted = page.waitForResponse(response => new URL(response.url()).pathname === path
    && response.request().method() === 'POST' && response.status() !== 429, { timeout: 70000 });
  await page.getByRole('button', { name: buttonName, exact: true }).click();
  const response = await accepted;
  expect(response.status(), `Calculation ${path} was accepted`).toBe(200);
}

/** A successful save follows asynchronous whole-plan validation. Preserve the
 * production quota window, but surface a rejected plan immediately. */
export async function saveMeasurement(page: Page): Promise<void> {
  return saveValidatedDialog(page, 'Save measurement');
}
export async function saveValidatedDialog(page: Page, buttonName: string): Promise<void> {
  test.setTimeout(Math.max(test.info().timeout, 180000));
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: buttonName, exact: true }).click();
  let rejected = '';
  await expect.poll(async () => {
    if (!await dialog.isVisible()) return true;
    const error = dialog.getByRole('alert').first();
    if (await error.isVisible()) { rejected = await error.innerText(); return true; }
    return false;
  }, { timeout: 70000, message: `Waiting for whole-plan validation: ${buttonName}` }).toBe(true);
  if (rejected) throw new Error(`${buttonName} was rejected: ${rejected}`);
  await expect(dialog).not.toBeVisible();
}

/** Test-side authoritative reads follow the same explicit-rejection contract as
 * the UI. An empty 429 body is never mistaken for malformed run JSON. */
export async function getWithQuota(request: APIRequestContext, path: string): Promise<APIResponse> {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await request.get(path);
    if (response.status() === 429) { await waitForQuota(response.headers()['retry-after']); continue; }
    expect(response.ok(), `Authoritative read ${path}: HTTP ${response.status()}`).toBe(true);
    return response;
  }
  throw new Error(`Read quota did not recover for ${path}.`);
}

/** Explicit rejection may be retried. This helper never retries an ambiguous
 * accepted mutation and never changes the application's production quotas. */
export async function cancelOwnedRun(request: APIRequestContext, id: string): Promise<void> {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await request.delete(`/api/runs/${encodeURIComponent(id)}`);
    if (response.ok() || response.status() === 404) return;
    // The API deliberately rejects cancellation of terminal history. Verify
    // that state instead of accepting every conflict (which could leave a
    // queued/running job with a missing cancellation token behind).
    if (response.status() === 409) {
      const snapshot = await request.get(`/api/runs/${encodeURIComponent(id)}`);
      if (snapshot.status() === 404) return;
      if (snapshot.ok()) {
        expect(['completed', 'failed', 'cancelled']).toContain((await snapshot.json()).status);
        return;
      }
      if (snapshot.status() !== 429) throw new Error(`Owned run ${id} could not be reconciled: HTTP ${snapshot.status()}.`);
      await waitForQuota(snapshot.headers()['retry-after']);
      continue;
    }
    if (response.status() !== 429) throw new Error(`Owned run ${id} could not be cancelled: HTTP ${response.status()}.`);
    await waitForQuota(response.headers()['retry-after']);
  }
  throw new Error(`Cancellation quota did not recover for owned test run ${id}.`);
}
async function waitForQuota(retryAfter?: string): Promise<void> {
  const seconds = Number(retryAfter ?? '1');
  await new Promise(resolve => setTimeout(resolve, Number.isFinite(seconds) ? Math.min(65000, Math.max(1000, seconds * 1000)) : 1000));
}

export const test = base.extend<{ ownedRuns: void }>({
  ownedRuns: [async ({ page, request }, runTest) => {
    const ids = new Set<string>(), reads: Promise<void>[] = [];
    const capture = (response: BrowserResponse) => {
      if (new URL(response.url()).pathname !== '/api/runs' || !response.ok() || response.request().method() !== 'POST') return;
      reads.push(response.json().then(run => { if (typeof run.id === 'string' && /^[a-f0-9]{32}$/.test(run.id)) ids.add(run.id); }).catch(() => {}));
    };
    page.on('response', capture);
    try { await runTest(); }
    finally {
      page.off('response', capture);
      await Promise.all(reads);
      // Stop audits/reconnects before quota-aware cleanup. A failed browser
      // assertion must never leave a 10-million-round job occupying CI workers.
      if (ids.size && !page.isClosed()) await page.goto('about:blank').catch(() => {});
      for (const id of ids) await cancelOwnedRun(request, id);
    }
  }, { auto: true, timeout: 150000 }],
});
