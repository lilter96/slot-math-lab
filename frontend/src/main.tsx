import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './api/client';
import { useAppStore } from './store';
import App from './App';
import '@xyflow/react/dist/style.css';
import './index.css';
// Live and retained evidence share controls, cards and responsive layouts.
// Load their styles before either lazy route, including direct Results links.
import './components/simulate/measurements.css';
import './components/simulate/evidence.css';

if (import.meta.env.DEV) {
  (window as unknown as Record<string, unknown>).__store = useAppStore;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
