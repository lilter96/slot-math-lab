import { expect, test } from '@playwright/test';
import path from 'node:path';

test('Reference canvas renders all thirty initial cells with the correct distinct artwork', async ({ page }) => {
  await page.goto('/play');
  await expect(page.locator('.nl-spin')).toBeEnabled({ timeout: 30000 });
  const cells = await page.locator('.nl-stage canvas').evaluate(async element => {
    const names = ['BONE', 'FISH', 'YARN', 'CANDLE', 'CAT'];
    const images = await Promise.all(names.map(async name => {
      const image = new Image();
      image.src = `/assets/images/nine-lives/${name.toLowerCase()}.webp`;
      await image.decode();
      return image;
    }));
    const actual = document.createElement('canvas');
    actual.width = 746; actual.height = 632;
    const context = actual.getContext('2d')!;
    // Read the live GPU canvas after its ticker has rendered this frame.
    await new Promise<void>(resolve => requestAnimationFrame(() => {
      context.drawImage(element as HTMLCanvasElement, 0, 0, 746, 632);
      resolve();
    }));
    const expected = document.createElement('canvas');
    expected.width = 116; expected.height = 116;
    const oracle = expected.getContext('2d')!;
    return Array.from({ length: 30 }, (_, position) => {
      const reel = Math.floor(position / 5), row = position % 5;
      const pixels = context.getImageData(10 + reel * 122, 10 + row * 122, 116, 116).data;
      const errors = images.map(image => {
        oracle.fillStyle = (reel + row) % 2 ? '#241817' : '#1c1312';
        oracle.fillRect(0, 0, 116, 116);
        oracle.drawImage(image, 0, 0, 116, 116);
        const reference = oracle.getImageData(0, 0, 116, 116).data;
        let error = 0, samples = 0;
        // Ignore rounded borders; compare RGB against independent native image decoding.
        for (let y = 4; y < 112; y += 2) for (let x = 4; x < 112; x += 2) {
          const offset = (y * 116 + x) * 4;
          for (let channel = 0; channel < 3; channel++) {
            error += Math.abs(pixels[offset + channel] - reference[offset + channel]); samples++;
          }
        }
        return error / samples;
      });
      return { reel, row, errors, rendered: names[errors.indexOf(Math.min(...errors))] };
    });
  });
  for (const cell of cells) {
    expect(cell.rendered, JSON.stringify(cell)).toBe(['BONE', 'FISH', 'YARN', 'CANDLE', 'CAT'][cell.row]);
    expect(cell.errors[cell.row], JSON.stringify(cell)).toBeLessThan(12);
    expect(Math.min(...cell.errors.filter((_, index) => index !== cell.row)) - cell.errors[cell.row]).toBeGreaterThan(3);
  }
  await page.screenshot({ path: path.resolve('../docs/design/qa', `reference-rendering-${test.info().project.name}.png`), fullPage: true });
});
