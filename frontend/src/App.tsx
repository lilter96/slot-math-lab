import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import TweaksPanel from './components/TweaksPanel';
import Build from './pages/Build';
import Simulate from './pages/Simulate';
import Results from './pages/Results';
import Export from './pages/Export';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/build" replace />} />
          <Route path="build" element={<Build />} />
          <Route path="simulate" element={<Simulate />} />
          <Route path="results" element={<Results />} />
          <Route path="export" element={<Export />} />
        </Route>
      </Routes>
      <TweaksPanel />
    </BrowserRouter>
  );
}
