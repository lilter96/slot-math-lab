import { expect } from '../../frontend/node_modules/@playwright/test/index.mjs';

/** Positive-path launch evidence must come from the accepted request. Explicit
 * quota rejections belong to the UI retry contract, never to run JSON. Other
 * responses fail immediately so validation/authentication errors stay visible. */
export function waitForRunLaunch(page, timeout = 90000) {
  return page.waitForResponse(response => new URL(response.url()).pathname === '/api/runs'
    && response.request().method() === 'POST' && response.status() !== 429, { timeout })
    .then(response => {
      if (response.status() !== 202) throw new Error(`Run launch rejected: HTTP ${response.status()}`);
      return response;
    });
}
export async function saveMeasurement(page) {
  return saveValidatedDialog(page, 'Save measurement');
}
export async function saveValidatedDialog(page, buttonName) {
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
