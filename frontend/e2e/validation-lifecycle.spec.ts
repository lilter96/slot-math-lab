import { test, expect, type Page } from './fixtures';
import { readFile } from 'node:fs/promises';

async function heldValidation(page: Page, button: string) {
  await page.evaluate(() => {
    const host = window as unknown as { validationAborted: boolean };
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => { if (String(input).endsWith('/api/validate')) init?.signal?.addEventListener('abort', () => { host.validationAborted = true; }, { once: true }); return original(input, init); };
  });
  let received = false, finished = false, release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/validate', async route => { const response = await route.fetch(); expect(response.ok()).toBe(true); expect((await response.json()).isValid).toBe(true); received = true; await gate; await route.fulfill({ response }); finished = true; });
  await page.getByRole('button', { name: button, exact: true }).click();
  await expect.poll(() => received).toBe(true);
  return {
    aborted: async () => { await expect.poll(() => page.evaluate(() => (window as unknown as { validationAborted: boolean }).validationAborted)).toBe(true); },
    release: async () => { release(); await expect.poll(() => finished).toBe(true); await page.unroute('**/api/validate'); },
  };
}

for (const editor of ['free spins', 'payouts']) {
  test(`${editor} validation aborts on unmount and retains an unapplied draft`, async ({ page }) => {
    await page.goto(editor === 'free spins' ? '/model/features?project=nine-lives' : '/model/data?view=payouts&project=nine-lives');
    const label = editor === 'free spins' ? 'Awarded free spins' : 'Coins for 5 cells';
    await page.getByLabel(label).fill('2');
    const delayed = await heldValidation(page, `Validate & apply ${editor}`);
    await page.getByRole('tab', { name: 'Build', exact: true }).click();
    await delayed.aborted(); await delayed.release();
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export project', exact: true }).click();
    const saved = JSON.parse(await readFile((await (await download).path())!, 'utf8'));
    expect(editor === 'free spins' ? saved.initialState.freeSpins : saved.initialState.clusterPaytable[0]).toBe(editor === 'free spins' ? 9 : '1');
    await page.getByRole('tab', { name: editor === 'free spins' ? 'Features' : 'Model data', exact: true }).click();
    if (editor === 'payouts') await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click();
    await expect(page.getByLabel(label)).toHaveValue('2');
    await expect(page.getByRole('button', { name: editor === 'free spins' ? 'Undo free-spin change' : 'Undo payout change' })).toHaveCount(0);
  });
}

test('Payout validation aborts on source change and restore while its form stays mounted', async ({ page }) => {
  await page.goto('/model/data?view=payouts&project=nine-lives');
  await page.getByLabel('Coins for 5 cells').fill('2');
  const delayed = await heldValidation(page, 'Validate & apply payouts');
  await page.getByRole('button', { name: 'State & expressions', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Project data & expressions' });
  await dialog.getByLabel('Data table').selectOption('freeSpins');
  await dialog.getByLabel('State field value').fill('8');
  await dialog.getByLabel('State field value').fill('9');
  await delayed.aborted(); await delayed.release();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'Undo payout change' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Validate & apply payouts' })).toBeEnabled();
  await page.getByRole('button', { name: 'Validate & apply payouts' }).click();
  await expect(page.getByRole('button', { name: 'Undo payout change' })).toBeVisible();
  await page.getByRole('button', { name: 'Undo payout change' }).click();
  await expect(page.getByLabel('Coins for 5 cells')).toHaveValue('1');
});

test('Returning from a nested mechanic aborts validation even when the complete model is unchanged', async ({ page }) => {
  await page.goto('/overview?project=nine-lives');
  await page.getByRole('button', { name: /Free spins.*Open logic/ }).click();
  await expect(page.getByTestId('model-navigation')).toContainText('free-spin');
  await page.getByRole('tab', { name: 'Model data', exact: true }).click();
  await page.getByRole('tab', { name: 'Cluster payouts', exact: true }).click();
  await page.getByLabel('Coins for 5 cells').fill('2');
  const delayed = await heldValidation(page, 'Validate & apply payouts');
  // Opening the raw editor explicitly returns to the root while this payout form stays mounted.
  await page.getByRole('button', { name: 'State & expressions', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Project data & expressions' })).toBeVisible();
  await delayed.aborted(); await delayed.release();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'Undo payout change' })).toHaveCount(0);
  await expect(page.getByLabel('Coins for 5 cells')).toHaveValue('2');
  await expect(page.getByRole('button', { name: 'Validate & apply payouts' })).toBeEnabled();
});
