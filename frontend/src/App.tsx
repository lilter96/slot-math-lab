import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import TweaksPanel from './components/TweaksPanel';
import Build from './pages/Build';
import Overview from './pages/Overview';
import ModelData from './pages/ModelData';
import Features from './pages/Features';

import Export from './pages/Export';
import { lazy, Suspense, type ReactNode } from 'react';
import { DEFAULT_FEATURES, useFeaturesQuery } from './api/hooks';
const Simulate = lazy(() => import('./pages/Simulate'));
const Results = lazy(() => import('./pages/Results'));
const NineLives = lazy(() => import('./games/nine-lives/NineLives'));

/**
 * Fail-closed route guard: the /play route is only reachable when the backend
 * reports `play`. While the flags request is pending we wait (no flash-redirect);
 * a disabled flag or a failed request redirects to /build so a bookmarked or
 * hand-typed /play URL can never mount the disabled surface.
 */
function RequirePlay({ children }: { children: ReactNode }) {
  const { data, isPending } = useFeaturesQuery();
  if (isPending) return <p role="status">Loading game…</p>;
  if (!(data ?? DEFAULT_FEATURES).play) return <Navigate to="/build" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/overview" replace />} />
          <Route path="overview" element={<Overview />} />
          <Route path="model/features" element={<Features />} />
          <Route path="model/data" element={<ModelData />} />
          <Route path="build" element={<Build />} />
          <Route path="simulate" element={<Suspense fallback={<p>Loading simulation lab…</p>}><Simulate /></Suspense>} />
          <Route path="results" element={<Suspense fallback={<p role="status">Loading run evidence…</p>}><Results /></Suspense>} />
          <Route path="export" element={<Export />} />
          <Route
            path="play"
            element={<RequirePlay><Suspense fallback={<p>Loading game…</p>}><NineLives /></Suspense></RequirePlay>}
          />
        </Route>
      </Routes>
      <TweaksPanel />
    </BrowserRouter>
  );
}
