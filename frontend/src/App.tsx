import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import TweaksPanel from './components/TweaksPanel';
import Build from './pages/Build';

import Export from './pages/Export';
import { lazy, Suspense } from 'react';
const Simulate = lazy(() => import('./pages/Simulate'));
const Results = lazy(() => import('./pages/Results'));
const DogHouse = lazy(() => import('./games/doghouse/DogHouse'));

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/build" replace />} />
          <Route path="build" element={<Build />} />
          <Route path="simulate" element={<Suspense fallback={<p>Loading simulation lab…</p>}><Simulate /></Suspense>} />
          <Route path="results" element={<Suspense fallback={<p role="status">Loading run evidence…</p>}><Results /></Suspense>} />
          <Route path="export" element={<Export />} />
          <Route path="play" element={<Suspense fallback={<p>Loading game…</p>}><DogHouse /></Suspense>} />
        </Route>
      </Routes>
      <TweaksPanel />
    </BrowserRouter>
  );
}
