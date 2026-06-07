import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useEffect, useRef } from 'react';
import type { TabId } from '../store';
import { useAppStore } from '../store';

const TABS: { id: TabId; label: string; to: string }[] = [
  { id: 'build', label: 'Build', to: '/build' },
  { id: 'simulate', label: 'Simulate', to: '/simulate' },
  { id: 'results', label: 'Results', to: '/results' },
  { id: 'export', label: 'Export', to: '/export' },
];

export default function Layout() {
  const mainRef = useRef<HTMLElement>(null);
  const location = useLocation();
  const setTab = useAppStore((s) => s.setTab);

  // Sync tab state with route and focus main content on route change
  useEffect(() => {
    const tab = TABS.find((t) => location.pathname.startsWith(t.to));
    if (tab) setTab(tab.id);
    mainRef.current?.focus();
  }, [location.pathname, setTab]);

  return (
    <>
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>

      <header role="banner">
        <h1>Slot Math Lab</h1>
        <nav aria-label="Main navigation">
          <ul role="tablist">
            {TABS.map((tab) => (
              <li key={tab.id} role="presentation">
                <NavLink
                  to={tab.to}
                  role="tab"
                  aria-selected={location.pathname.startsWith(tab.to)}
                  className={({ isActive }) => (isActive ? 'tab active' : 'tab')}
                >
                  {tab.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </header>

      <main id="main-content" ref={mainRef} tabIndex={-1} aria-label="Main content">
        <Outlet />
      </main>

      <footer role="contentinfo">
        <p>Slot Math Lab &mdash; No-code slot math constructor</p>
      </footer>
    </>
  );
}
