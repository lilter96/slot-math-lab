import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import { useAppStore, MOOD_HUE, type Mood } from '../store';
import { Ic } from './Icons';

const TABS = [
  { id: 'build' as const, label: 'Build', Icon: Ic.build },
  { id: 'simulate' as const, label: 'Simulate', Icon: Ic.sim },
  { id: 'results' as const, label: 'Results', Icon: Ic.results },
  { id: 'export' as const, label: 'Export', Icon: Ic.export },
];

export default function Layout() {
  const location = useLocation();
  const tab = useAppStore((s) => s.tab);
  const setTab = useAppStore((s) => s.setTab);
  const configName = useAppStore((s) => s.configName);
  const tweaks = useAppStore((s) => s.tweaks);

  // Sync tab from URL
  useEffect(() => {
    const matched = TABS.find((t) => location.pathname.startsWith('/' + t.id));
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
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>

      <div className="topbar">
        <div className="brand">
          <div className="brand-mark" />
          <div className="brand-name">
            Slot Math <b>Lab</b>
          </div>
        </div>

        <div className="project-pill">
          <span className="dot" />
          {configName}
        </div>

        <div className="tabs" role="tablist" aria-label="Main navigation">
          {TABS.map(({ id, label, Icon }) => (
            <NavLink
              key={id}
              to={`/${id}`}
              role="tab"
              aria-selected={tab === id}
              className={({ isActive }) => 'tab' + (isActive ? ' active' : '')}
            >
              <Icon style={{ width: 14, height: 14 }} />
              {label}
            </NavLink>
          ))}
        </div>

        <div className="topbar-right">
          <div className="legend">
            <span className="item">
              <span className="sw" style={{ background: 'var(--exact)' }} />
              Exact
            </span>
            <span className="item">
              <span className="sw" style={{ background: 'var(--epsilon)' }} />
              ε-pruned
            </span>
            <span className="item">
              <span className="sw" style={{ background: 'var(--sampled)' }} />
              Sampled
            </span>
          </div>
          <button
            className="btn"
            style={{ borderColor: 'oklch(0.78 0.15 305 / 0.4)' }}
          >
            <span style={{ color: 'var(--n-loop)' }}>
              <Ic.ai style={{ width: 15, height: 15 }} />
            </span>
            AI assist
          </button>
        </div>
      </div>

      <div className="body" id="main-content">
        <Outlet />
      </div>
    </div>
  );
}
