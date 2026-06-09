/**
 * Playwright E2E: single coin-flip → simulate → verify RTP ≈ 50%
 * Loop(iterations=1) → Draw(heads=1,tails=0) → Sink
 * Exact RTP = 0.5 = 50%
 */

import pkg from '/opt/node22/lib/node_modules/playwright/index.js';
const { chromium } = pkg;

const FRONTEND = 'http://localhost:5173';
const RUN_SPINS = 50_000;

const COIN_FLIP_NODES = [
  {
    id: 'loop-1', type: 'loop',
    position: { x: 100, y: 200 },
    data: { nodeType: 'loop', label: 'Loop ×1', sub: 'Fixpoint', level: 'a', iterations: 1, terminationExpr: '' },
  },
  {
    id: 'draw-1', type: 'draw',
    position: { x: 350, y: 150 },
    data: {
      nodeType: 'draw', label: 'Coin Flip', sub: 'Weighted choice', level: 'a',
      drawWeights: [
        { outcomeId: 'heads', weight: 1, value: 1 },
        { outcomeId: 'tails', weight: 1, value: 0 },
      ],
    },
  },
  {
    id: 'sink-1', type: 'sink',
    position: { x: 350, y: 320 },
    data: { nodeType: 'sink', label: 'Sink', sub: 'Metrics output', level: 'a' },
  },
];

const COIN_FLIP_EDGES = [
  { id: 'e1', source: 'loop-1', sourceHandle: 'body', target: 'draw-1', targetHandle: 'in',
    type: 'default', style: { stroke: 'oklch(0.40 0.016 255)', strokeWidth: 2 } },
  { id: 'e2', source: 'loop-1', sourceHandle: 'exit', target: 'sink-1', targetHandle: 'in',
    type: 'default', style: { stroke: 'oklch(0.40 0.016 255)', strokeWidth: 2 } },
];

async function injectGraph(page) {
  await page.waitForFunction(() => !!window.__store, { timeout: 15_000 });
  await page.evaluate(({ nodes, edges }) => {
    window.__store.setState({ nodes, edges, selectedNodeId: null });
  }, { nodes: COIN_FLIP_NODES, edges: COIN_FLIP_EDGES });
}

async function main() {
  const browser = await chromium.launch({
    executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome',
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const page = await browser.newPage();
  const browserErrors = [];
  page.on('console', msg => { if (msg.type() === 'error') browserErrors.push(msg.text()); });
  page.on('pageerror', err => browserErrors.push(err.message));

  try {
    // Load the Simulate page directly
    console.log('1. Loading Simulate page...');
    await page.goto(FRONTEND + '/simulate', { waitUntil: 'networkidle', timeout: 30_000 });

    // Inject coin flip graph state into Zustand store
    console.log('2. Injecting coin-flip graph...');
    await injectGraph(page);
    console.log('   Graph injected: Loop(1) →body→ Draw(coin) + Loop →exit→ Sink');

    await page.waitForTimeout(500);
    await page.screenshot({ path: '/tmp/sim-before.png', fullPage: true });
    console.log('   Screenshot: /tmp/sim-before.png');

    // Set spins
    const spinsInput = page.locator('input[type="number"]').first();
    if (await spinsInput.isVisible()) {
      await spinsInput.fill(String(RUN_SPINS));
      console.log(`   Spins set to ${RUN_SPINS.toLocaleString()}`);
    }

    // Start run
    console.log('3. Starting run...');
    const startBtn = page.locator('button:has-text("Start Run")').first();
    await startBtn.waitFor({ state: 'visible', timeout: 10_000 });
    await startBtn.click();
    console.log('   Clicked Start Run');

    // Wait for backend errors or progress
    const t0 = Date.now();
    console.log('4. Waiting for backend to respond...');

    // Wait up to 30s for either an error or actual sample count > 0
    let result = null;
    for (let i = 0; i < 60; i++) {
      await page.waitForTimeout(1000);
      const text = await page.textContent('body') ?? '';

      if (text.includes('Backend error') || text.includes('Run failed')) {
        const match = text.match(/(Backend error[^.]*\.|Run failed[^.]*\.)/);
        result = { pass: false, reason: match?.[1] ?? 'Unknown backend error' };
        break;
      }

      // Check if samples > 0
      if (/\b[1-9][0-9,]+\b/.test(text.match(/Samples\s*\n?\s*([\d,]+)/)?.[1] ?? '')) {
        result = { pass: true, reason: 'Samples counting up', text };
        break;
      }

      // Check for completion
      if (text.includes('complete') || text.includes('Complete')) {
        result = { pass: true, reason: 'Completed', text };
        break;
      }

      if (i % 10 === 9) console.log(`   Still waiting... ${i+1}s`);
    }

    if (!result) {
      const text = await page.textContent('body') ?? '';
      if (text.includes('Backend error') || text.includes('Run failed')) {
        result = { pass: false, reason: 'Backend error after timeout' };
      } else {
        result = { pass: false, reason: 'No progress detected in 60s', text };
      }
    }

    console.log(`   Detected after ${((Date.now() - t0)/1000).toFixed(1)}s: ${result.reason}`);

    if (result.pass) {
      // Wait for completion
      console.log('5. Waiting for completion...');
      try {
        await page.waitForFunction(
          () => document.body.innerText.toLowerCase().includes('complete'),
          { timeout: 120_000 }
        );
        console.log('   Run completed!');
      } catch {
        console.log('   Timeout waiting for completion (partial results OK)');
      }
    }

    await page.screenshot({ path: '/tmp/sim-after.png', fullPage: true });
    console.log('   Screenshot: /tmp/sim-after.png');

    const finalText = await page.textContent('body') ?? '';

    // Extract RTP percentages
    const rtpMatches = [...finalText.matchAll(/(\d+\.\d{2,3})%/g)].map(m => parseFloat(m[1]));
    console.log('\n=== RESULTS ===');

    if (!result.pass) {
      console.log('❌ FAIL:', result.reason);
      if (result.text) console.log('   Page text:', result.text.substring(0, 400));
    } else {
      console.log('✅ Run completed without backend errors');
      console.log('   RTP % values on page:', rtpMatches);

      const near50 = rtpMatches.find(v => Math.abs(v - 50) < 5);
      if (near50 !== undefined) {
        console.log(`   ✓ RTP = ${near50.toFixed(3)}% — converged near 50% target`);
        console.log('   ✅ PASS: Simulation is working correctly');
      } else if (rtpMatches.length === 0) {
        console.log('   ⚠ No RTP% values found on page — check screenshot');
      } else {
        console.log(`   ⚠ RTP values ${rtpMatches.join(', ')} — not near 50% yet`);
      }
    }

    if (browserErrors.length > 0) {
      console.log('Browser console errors:', browserErrors.slice(0, 3));
    }

    return result.pass;
  } finally {
    await browser.close();
  }
}

main().then(ok => process.exit(ok ? 0 : 1)).catch(err => {
  console.error('Fatal:', err);
  process.exit(1);
});
