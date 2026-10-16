import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { BASE_URL, queryClient } from './api/client';
import { readBrowserIdentity, setBrowserIdentity, type BrowserIdentity } from './lib/browserIdentity';
import '@xyflow/react/dist/style.css';
import './index.css';
// Live and retained evidence share controls, cards and responsive layouts.
// Load their styles before either lazy route, including direct Results links.
import './components/simulate/measurements.css';
import './components/simulate/evidence.css';

const root = createRoot(document.getElementById('root')!);

// Verify identity before any store reads browser drafts. Never select a private
// storage namespace from a cached or user-entered identity.
async function start() {
  let identity: BrowserIdentity;
  try {
    identity = await readBrowserIdentity(`${BASE_URL}/api/auth/status`, pause => root.render(<section className="projects-page"><h1>Checking your session</h1><p role="status">The server requested a {Math.ceil(pause / 1000)}s pause. Your saved projects will open after verification.</p></section>));
  } catch {
    root.render(<section className="projects-page"><h1>Workspace unavailable</h1><p role="alert">Unable to verify your session. Your saved projects have not been opened.</p><button className="btn" onClick={() => location.reload()}>Retry connection</button></section>);
    return;
  }
  setBrowserIdentity(identity);
  if (identity.required && !identity.authenticated) {
    const { default: Login } = await import('./components/Login');
    root.render(<Login forceRequired />);
    return;
  }
  const [{ default: App }, { useAppStore }] = await Promise.all([import('./App'), import('./store')]);
  if (import.meta.env.DEV) (window as unknown as Record<string, unknown>).__store = useAppStore;
  root.render(
    <StrictMode><QueryClientProvider client={queryClient}><App /></QueryClientProvider></StrictMode>,
  );
}
void start();
