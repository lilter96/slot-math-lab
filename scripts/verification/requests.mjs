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
