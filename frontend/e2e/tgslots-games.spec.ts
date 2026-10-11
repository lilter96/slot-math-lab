import { test, expect } from './fixtures';

for (const game of ['nine-lives', 'x7-club', 'ancient-dragon', 'woodland-whisper', 'le-militare']) {
  test(`${game} plays its JSON graph and the API reproduces the paid round`, async ({ page }, info) => {
    test.setTimeout(120000);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      const fill = crypto.getRandomValues.bind(crypto);
      Object.defineProperty(crypto, 'getRandomValues', { value: (bytes: Uint32Array) => {
        const result = fill(bytes);
        if (bytes instanceof Uint32Array && bytes.length === 1) result[0] = 42;
        return result;
      } });
    });
    await page.goto(`/games.html?game=${game}`);
    const standalone = game === 'nine-lives' || game === 'x7-club';
    const spin = page.getByRole('button', { name: /^(?:↻\s*)?SPIN$/ });
    if (standalone) await expect(spin).toBeEnabled({ timeout: 60000 });
    else {
      await page.getByRole('button', { name: 'Start game', exact: true }).click({ timeout: 60000 });
      await expect(page.locator('#game-loader')).toHaveCount(0);
      await expect(page.locator('canvas')).toBeVisible();
    }
    await info.attach(`${game}-loaded`, { body: await page.screenshot(), contentType: 'image/png' });
    const played = page.waitForResponse(response => new URL(response.url()).pathname === '/api/play/round'
      && response.request().method() === 'POST' && response.status() !== 429);
    if (standalone) await spin.click();
    else await page.keyboard.press('Space');
    const response = await played;
    expect(response.ok()).toBe(true);
    const request = response.request().postDataJSON();
    const round = await response.json();
    expect(request.seed).toBe(42);
    expect(round.seed).toBe(42);
    expect(round.configHash).toHaveLength(64);
    expect(request.config.mechanics).toBeTruthy();
    const replay = await page.request.post('/api/play/round', { data: request });
    expect(replay.ok()).toBe(true);
    expect(await replay.json()).toEqual(round);
    if (standalone) await expect(page.locator(game === 'nine-lives' ? '.nl-spin' : '.x7-spin')).toBeEnabled({ timeout: 30000 });
    expect(errors).toEqual([]);
  });
}

for (const game of ['nine-lives', 'x7-club']) {
  test(`${game} retains startup recovery when rendering cannot initialize`, async ({ page }) => {
    await page.addInitScript(() => {
      HTMLCanvasElement.prototype.getContext = (() => null) as typeof HTMLCanvasElement.prototype.getContext;
    });
    await page.goto(`/games.html?game=${game}`);
    await expect(page.locator('#game-loader .loader-status')).toHaveText('Unable to load the game. Please retry.');
    await expect(page.getByRole('button', { name: 'Retry loading' })).toBeVisible();
  });
}

test('Two tabs serialize a wallet command and reject the losing stale revision', async ({ page, context }) => {
  test.setTimeout(120000);
  await context.addInitScript(() => {
    const fill = crypto.getRandomValues.bind(crypto);
    Object.defineProperty(crypto, 'getRandomValues', { value: (bytes: Uint32Array) => {
      const result = fill(bytes);
      if (bytes instanceof Uint32Array && bytes.length === 1) result[0] = 42;
      return result;
    } });
  });
  await page.goto('/games.html?game=x7-club');
  const firstSpin = page.getByRole('button', { name: /^(?:↻\s*)?SPIN$/ });
  await expect(firstSpin).toBeEnabled({ timeout: 60000 });
  const second = await context.newPage();
  await second.goto('/games.html?game=x7-club');
  const secondSpin = second.getByRole('button', { name: /^(?:↻\s*)?SPIN$/ });
  await expect(secondSpin).toBeEnabled({ timeout: 60000 });
  let release!: () => void;
  const hold = new Promise<void>(resolve => { release = resolve; });
  let entered!: () => void;
  const started = new Promise<void>(resolve => { entered = resolve; });
  let calls = 0;
  await context.route('**/api/play/round', async route => {
    calls++;
    entered();
    const response = await route.fetch();
    await hold;
    await route.fulfill({ response });
  });
  try {
    await firstSpin.click();
    await started;
    await secondSpin.click();
    release();
    await expect(second.locator('.x7-error')).toContainText('Stale revision', { timeout: 30000 });
    await expect(firstSpin).toBeEnabled({ timeout: 30000 });
    expect(calls).toBe(1);
    const snapshot = await page.evaluate(() => {
      const id = localStorage.getItem('tgslots:x7-club:session');
      return JSON.parse(localStorage.getItem(`slot-math-lab:game-session:${id}`)!);
    });
    expect(snapshot.started).toBe(1);
    expect(snapshot.wallet.revision).toBe(1);
    expect(snapshot.wallet.answered).toHaveLength(1);
  } finally {
    release();
    await context.unrouteAll({ behavior: 'wait' });
    await second.close();
  }
});

test('A timed-out transport releases the UI and retries the same unpaid session round', async ({ page }) => {
  test.setTimeout(120000);
  await page.goto('/games.html?game=x7-club');
  const spin = page.locator('.x7-spin');
  await expect(spin).toBeEnabled({ timeout: 60000 });
  await page.evaluate(() => {
    const timeout = AbortSignal.timeout.bind(AbortSignal);
    AbortSignal.timeout = () => timeout(100);
  });
  let release!: () => void;
  const hold = new Promise<void>(resolve => { release = resolve; });
  let firstBody: unknown;
  await page.route('**/api/play/round', async route => {
    firstBody = route.request().postDataJSON();
    const response = await route.fetch();
    await hold;
    await route.fulfill({ response }).catch(() => {});
  });
  try {
    await spin.click();
    await expect(spin).toHaveText('RETRY', { timeout: 10000 });
    await expect(spin).toBeEnabled();
    const snapshot = await page.evaluate(() => {
      const id = localStorage.getItem('tgslots:x7-club:session');
      return JSON.parse(localStorage.getItem(`slot-math-lab:game-session:${id}`)!);
    });
    expect(snapshot.started).toBe(0);
    expect(snapshot.wallet.revision).toBe(0);
  } finally {
    release();
    await page.unrouteAll({ behavior: 'wait' });
  }
  await page.evaluate(() => { AbortSignal.timeout = () => new AbortController().signal; });
  const retried = page.waitForRequest(request => new URL(request.url()).pathname === '/api/play/round');
  await spin.click();
  expect((await retried).postDataJSON()).toEqual(firstBody);
  await expect(spin).toBeEnabled({ timeout: 30000 });
  const revision = await page.evaluate(() => {
    const id = localStorage.getItem('tgslots:x7-club:session');
    return JSON.parse(localStorage.getItem(`slot-math-lab:game-session:${id}`)!).wallet.revision;
  });
  expect(revision).toBe(1);
});
