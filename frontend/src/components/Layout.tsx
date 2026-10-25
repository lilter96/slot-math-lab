import {useModelSnapshot} from '../hooks/useModelProjection';
import {captureProjectDraft, getDraftRecovery, getDraftStorageError, type Mood, MOOD_HUE, useAppStore} from '../store';
import Login from './Login';
import TweaksPanel from './TweaksPanel';
import {useProjectInitialization} from '../hooks/useProjectInitialization';
import AiGenerateModal from './AiGenerateModal';
import {getBrowserIdentity} from '../lib/browserIdentity';
import {NavLink, Outlet, useLocation} from 'react-router-dom';
import {useEffect, useState} from 'react';
import {Ic} from './Icons';
import {useSimulationConnection} from '../hooks/useSimulation';
import {DEFAULT_FEATURES, useFeaturesQuery} from '../api/hooks';

const TABS = [
    {id: 'projects' as const, label: 'My projects', Icon: Ic.build},
    {id: 'overview' as const, label: 'Overview', Icon: Ic.build},
    {id: 'data' as const, path: 'model/data', label: 'Model data', Icon: Ic.draw},
    {id: 'studio' as const, path: 'model/studio', label: 'Studio', Icon: Ic.evaluator},
    {id: 'machine' as const, path: 'model/machine', label: 'State machine', Icon: Ic.loop},
    {id: 'features' as const, path: 'model/features', label: 'Features', Icon: Ic.loop},
    {id: 'build' as const, label: 'Build', Icon: Ic.build},
    {id: 'simulate' as const, label: 'Simulate', Icon: Ic.sim},
    {id: 'results' as const, label: 'Results', Icon: Ic.results},
    {id: 'export' as const, label: 'Export', Icon: Ic.export},
    {id: 'debug' as const, label: 'Debug', Icon: Ic.sim},
    {id: 'play' as const, label: 'Reference demo', Icon: Ic.sim},
];

export default function Layout() {
    const modelSnapshot = useModelSnapshot();
    const importError = useProjectInitialization();
    useSimulationConnection();
    const location = useLocation();
    const tab = useAppStore((s) => s.tab);
    const setTab = useAppStore((s) => s.setTab);
    const configName = useAppStore(s => !s.projectId ? null : s.graphTrail.length ? String(s.graphTrail[0].graph.name ?? s.configName ?? 'Untitled model') : s.configName ?? 'Untitled model');
    const tweaks = useAppStore((s) => s.tweaks);
    const features = useFeaturesQuery().data ?? DEFAULT_FEATURES;

    const [storageError, setStorageError] = useState(getDraftStorageError);
    useEffect(() => {
        const update = (event: Event) => setStorageError((event as CustomEvent<string | null>).detail);
        window.addEventListener('slotmath:draft-storage', update);
        return () => window.removeEventListener('slotmath:draft-storage', update);
    }, []);
    const [showAi, setShowAi] = useState(false);
    const [collapsed, setCollapsed] = useState(() => sessionStorage.getItem('slotmath-nav-collapsed') === 'true');
    const toggleNav = () => setCollapsed(value => {
        sessionStorage.setItem('slotmath-nav-collapsed', String(!value));
        return !value;
    });

    // Fail-closed: without a confirmed `play` flag the Play entry is dropped.
    const tabs = TABS.filter(({id}) => (id !== 'play' && id !== 'debug') || features.play);

    // Sync tab from URL
    useEffect(() => {
        const matched = TABS.find((t) => location.pathname.startsWith('/' + ('path' in t ? t.path : t.id)));
        if (matched && matched.id !== tab) setTab(matched.id);
    }, [location.pathname, tab, setTab]);

    // Apply tweaks to document
    useEffect(() => {
        const r = document.documentElement.style;
        r.setProperty('--exact', tweaks.accent);
        r.setProperty('--exact-dim', `color-mix(in srgb, ${tweaks.accent} 16%, transparent)`);
        r.setProperty('--n-eval', tweaks.accent);
        r.setProperty('--hue', String(MOOD_HUE[tweaks.mood as Mood] ?? 255));
        document.body.classList.toggle('no-grid', !tweaks.grid);
        document.body.classList.toggle('no-flow', !tweaks.flow);
        document.body.classList.toggle('compact', tweaks.density === 'compact');
    }, [tweaks]);

    return (
        <div className="app">
            <Login/>
            {showAi && features.ai && <AiGenerateModal onClose={() => setShowAi(false)}/>}
            <a href="#main-content" className="skip-link">
                Skip to main content
            </a>

            <div className="topbar">
                <div className="brand">
                    <div className="brand-mark"/>
                    <div className="brand-name">
                        Slot Math <b>Lab</b>
                    </div>
                </div>

                <NavLink to="/projects" className="project-pill" aria-label="Choose project"
                         aria-description={configName ? `Current project: ${configName}` : 'No project selected'}
                         title={configName ?? 'Choose a project'}>
                    <span className="dot"/>
                    <span className="project-name">{configName ?? 'Choose a project'}</span>
                </NavLink>

                <div className="topbar-right">{configName && location.pathname !== '/model/studio' &&
                    <NavLink className="btn primary" to="/simulate">Run analysis</NavLink>}
                    {features.ai && <button className="btn" onClick={() => setShowAi(true)}>AI assist</button>}
                    {/* The ported games are a page of their own: they play their graphs through the same endpoint as /play. */}
                    {features.play && (
                        <a className="btn" href="/games.html" style={{textDecoration: 'none'}}>
                            <Ic.sim style={{width: 15, height: 15}}/>
                            Games
                        </a>
                    )}
                    {getBrowserIdentity().authenticated && <button className="btn" onClick={async () => {
                        const response = await fetch('/api/auth/logout', {method: 'POST'});
                        if (response.ok) window.location.reload();
                    }}>Sign out</button>}
                    <TweaksPanel/>
                </div>
            </div>

            {modelSnapshot.error && <div className="draft-storage-warning" role="alert">Model cannot be
                serialized: {modelSnapshot.error} Your editable draft is retained.
                <NavLink className="btn" to="/build">Edit model</NavLink>
                <button className="btn" onClick={() => {
                    const url = URL.createObjectURL(new Blob([JSON.stringify({format: 'slotmath-editable-draft-v1', ...captureProjectDraft()}, null, 2)], {type: 'application/json'}));
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = 'slotmath-editable-draft.json';
                    link.click();
                    URL.revokeObjectURL(url);
                }}>Download editable draft
                </button>
            </div>}
            {storageError && <div className="draft-storage-warning" role="alert">{storageError} <NavLink className="btn"
                                                                                                         to="/export">Back
                up this project</NavLink>{getDraftRecovery() !== null && <button className="btn" onClick={() => {
                const original = getDraftRecovery();
                if (original === null) return;
                const url = URL.createObjectURL(new Blob([original], {type: 'application/json'}));
                const link = document.createElement('a');
                link.href = url;
                link.download = 'slotmath-workspace-recovery.json';
                link.click();
                URL.revokeObjectURL(url);
            }}>Download stored workspace</button>}</div>}
            <div className={'workspace-shell' + (collapsed ? ' nav-collapsed' : '')}>
                <aside className="workspace-nav" aria-label="Workspace">
                    <button className="btn nav-toggle"
                            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
                            aria-expanded={!collapsed}
                            onClick={toggleNav}>{collapsed ? '☰' : 'Collapse navigation'}</button>
                    <span className="nav-project">{configName ?? 'Choose a project'}</span>
                    <div className="tabs" role="tablist" aria-label="Main navigation" onKeyDown={event => {
                        if (!['ArrowDown', 'ArrowUp', 'ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
                        const items = Array.from(event.currentTarget.querySelectorAll<HTMLAnchorElement>('[role=tab]'));
                        const current = items.indexOf(document.activeElement as HTMLAnchorElement);
                        const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (current + (['ArrowDown', 'ArrowRight'].includes(event.key) ? 1 : -1) + items.length) % items.length;
                        event.preventDefault();
                        items[next]?.focus();
                        items[next]?.click();
                    }}>
                        {tabs.map((item) => (
                            <NavLink
                                key={item.id}
                                to={`/${'path' in item ? item.path : item.id}`}
                                role="tab"
                                aria-selected={tab === item.id}
                                aria-label={item.label}
                                title={item.label}
                                className={({isActive}) => 'tab' + (isActive ? ' active' : '')}
                            >
                                <item.Icon style={{width: 14, height: 14}}/>
                                {item.label}
                            </NavLink>
                        ))}
                    </div>


                </aside>
                <main className="body" id="main-content" tabIndex={-1}>{importError &&
                    <p role="alert">{importError}</p>}<Outlet/></main>
            </div>
        </div>
    );
}
