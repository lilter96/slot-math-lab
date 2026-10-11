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
