import {BrowserRouter, Navigate, Route, Routes} from 'react-router-dom';
import Layout from './components/Layout';
import Build from './pages/Build';
import Overview from './pages/Overview';
import Projects from './pages/Projects';
import {useAppStore} from './store';
import ModelData from './pages/ModelData';
import Features from './pages/Features';

import Export from './pages/Export';
import {lazy, type ReactNode, Suspense} from 'react';
import {DEFAULT_FEATURES, useFeaturesQuery} from './api/hooks';

const Simulate = lazy(() => import('./pages/Simulate'));
const Results = lazy(() => import('./pages/Results'));
const Debug = lazy(() => import('./pages/Debug'));
const NineLives = lazy(() => import('./games/nine-lives/NineLives'));
const ReelPaytableStudio = lazy(() => import('./pages/ReelPaytableStudio'));
const StateMachineDesigner = lazy(() => import('./pages/StateMachineDesigner'));

/**
 * Fail-closed route guard: the /play route is only reachable when the backend
 * reports `play`. While the flags request is pending we wait (no flash-redirect);
 * a disabled flag or a failed request redirects to /build so a bookmarked or
 * hand-typed /play URL can never mount the disabled surface.
 */
function RequirePlay({children}: { children: ReactNode }) {
    const {data, isPending} = useFeaturesQuery();
    if (isPending) return <p role="status">Loading game…</p>;
    if (!(data ?? DEFAULT_FEATURES).play) return <Navigate to="/build" replace/>;
    return <>{children}</>;
}

export default function App() {
    const projectId = useAppStore(s => s.projectId);
    return (
        <BrowserRouter>
            <Routes>
                <Route element={<Layout/>}>
                    <Route index element={<Navigate to="/projects" replace/>}/>
                    <Route path="projects" element={<Projects/>}/>
                    <Route path="overview" element={<Overview/>}/>
                    <Route path="model/features" element={<Features/>}/>
                    <Route path="model/data" element={<ModelData/>}/>
                    <Route path="model/studio" element={<Suspense
                        fallback={<p role="status">Loading Studio…</p>}><ReelPaytableStudio/></Suspense>}/>
                    <Route path="model/machine" element={<Suspense fallback={<p role="status">Loading State Machine
                        Designer…</p>}><StateMachineDesigner/></Suspense>}/>
                    <Route path="build" element={<Build/>}/>
                    <Route path="simulate"
                           element={<Suspense fallback={<p>Loading simulation lab…</p>}><Simulate/></Suspense>}/>
                    <Route path="results" element={<Suspense
                        fallback={<p role="status">Loading run evidence…</p>}><Results/></Suspense>}/>
                    <Route path="export" element={<Export/>}/>
                    <Route path="debug"
                           element={<RequirePlay><Suspense fallback={<p>Loading debugger…</p>}><Debug key={projectId}/></Suspense></RequirePlay>}/>
                    <Route
                        path="play"
                        element={<RequirePlay><Suspense
                            fallback={<p>Loading game…</p>}><NineLives/></Suspense></RequirePlay>}
                    />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
