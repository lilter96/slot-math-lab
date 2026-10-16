import { readBrowserIdentity, setBrowserIdentity } from '../../lib/browserIdentity';

// Demo sessions use the same verified account namespace as the workbench.
async function start() {
  const identity = await readBrowserIdentity((import.meta.env.VITE_API_URL ?? '') + '/api/auth/status', pause => {
    const status = document.querySelector('.loader-status');
    if (status) status.textContent = `Checking your session after the server's ${Math.ceil(pause / 1000)}s pause…`;
  });
  if (identity.required && !identity.authenticated) { location.replace('/projects'); return; }
  setBrowserIdentity(identity);
  await import('./main');
}
void start().catch(() => {
  const status = document.querySelector('.loader-status');
  if (status) status.textContent = 'Unable to verify your session. Saved games have not been opened.';
  const button = document.createElement('button'); button.textContent = 'Retry connection'; button.className = 'loader-retry';
  button.addEventListener('click', () => location.reload());
  document.querySelector('.loader-content')?.append(button);
});
