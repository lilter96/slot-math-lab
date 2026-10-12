# Reference artwork rendering — 2026-10-12

## Reproduced defect and correction

The ready WebKit production demo displayed empty/repeated symbols instead of the initial BONE/FISH/YARN/CANDLE/CAT rows. The previous `reference-stable-webkit-1440.png` preserves the failure. Inspection of pixi-reels replacement showed preserved identity/positions. An isolated render of original and generated textures reproduced corruption with ImageBitmap; disabling mipmaps alone did not correct it. Native HTML image decoding/upload rendered both rows correctly.

The reference AssetRegistry now sets Pixi's `preferCreateImageBitmap: false` preference before loading. This is a shared Assets-parser preference for subsequent loads in that page, not browser-name dispatch. The other standalone game entry point has a separate registry and was not changed. Assets still own caching/unloading; generated cell-sized textures, pool mechanics, mathematical model and engine remain unchanged. Native image loading forgoes the worker bitmap path; no loading-throughput improvement is claimed.

## Independent evidence

New `e2e/reference-rendering.spec.ts` reads the actual GPU canvas after a ticker frame. It decodes source images independently through native Image, draws each expected candidate over the known cell background, and compares sampled RGB pixels in all thirty cell interiors. Every cell must identify its expected artwork, have mean RGB error below 12, and differ from all wrong candidates by more than 3. No DOM symbol label or API state substitutes for pixels.

- Before fix: WebKit 0/1; top BONE cell did not match. `reference-rendering-before.log` retains the failure.
- Build and exact-file ESLint pass (`reference-rendering-build.log`).
- New/affected Chromium 3/3; Firefox/WebKit 6/6 (`reference-rendering-chromium.log`, `reference-rendering-cross.log`). Includes the pixel oracle, delayed-renderer readiness and actual seeded spin/replay with unchanged credits.
- Refreshed four-size WebKit legibility/targets/capture 1/1 (`reference-rendering-viewports.log`): 1440×900, 1280×800, 1024×768, 390×844. No document overflow. Desktop and phone captures visually inspected; inner scrolling remains necessary on phone.
- `reference-rendering-{chromium,firefox,webkit}.png` and refreshed `reference-legibility-{width}.png` show corrected distinct artwork. Initial display is authored presentation, not calculated evidence.

## Acceptance limits

Reference agent score improves from 13 to 14/16 by restoring domain fidelity. Canvas accessibility remains partial; this is not human practitioner acceptance. The oracle covers initial stationary artwork, not every cascade animation frame or every Safari hardware/software combination. Existing replay checks verify response/credit behavior separately.

No full suite, backend/kernel/PRNG change or deployment. Required scoped Rider attempts exit 3 with missing solution context; Reformat/Full Cleanup remain unverified, listed in RIDER_CLEANUP_REQUIRED.md. Next work remains safe-integer JSON ingestion/export, command palette, real compositor performance and practitioner/Core/Rider gates.
