import { useState } from 'react';
import { useAppStore, type Mood, type Density } from '../store';

const ACCENT_OPTIONS = ['#46d39a', '#34d3c2', '#7ed35a', '#37c6e0'];
const MOOD_OPTIONS: { value: Mood; label: string }[] = [
  { value: 'slate', label: 'Slate' },
  { value: 'ocean', label: 'Ocean' },
  { value: 'violet', label: 'Violet' },
  { value: 'steel', label: 'Steel' },
];
const DENSITY_OPTIONS: { value: Density; label: string }[] = [
  { value: 'regular', label: 'Regular' },
  { value: 'compact', label: 'Compact' },
];

export default function TweaksPanel() {
  const tweaks = useAppStore((s) => s.tweaks);
  const setTweak = useAppStore((s) => s.setTweak);
  const [collapsed, setCollapsed] = useState(true);

  return (
    <div
      style={{
        position: 'fixed',
        left: 16,
        bottom: 16,
        zIndex: 100,
        width: 240,
        background: 'var(--bg-1)',
        border: '1px solid var(--line-2)',
        borderRadius: 12,
        boxShadow: 'var(--shadow-pop)',
        fontSize: '11.5px',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 14px',
          cursor: 'pointer',
          userSelect: 'none',
          borderBottom: collapsed ? 'none' : '1px solid var(--line)',
        }}
        onClick={() => setCollapsed(!collapsed)}
      >
        <b style={{ fontSize: 12, fontWeight: 600 }}>Tweaks</b>
        <span style={{ color: 'var(--faint)', fontSize: 10 }}>
          {collapsed ? '▶' : '▼'}
        </span>
      </div>

      {!collapsed && (
        <div
          style={{
            padding: '2px 14px 14px',
            display: 'flex',
            flexDirection: 'column',
            gap: 10,
            maxHeight: 'calc(100vh - 120px)',
            overflowY: 'auto',
          }}
        >
          {/* Accent */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Accent
            </div>
            <div style={{ display: 'flex', gap: 6 }}>
              {ACCENT_OPTIONS.map((color) => (
                <button
                  key={color}
                  aria-label={`Accent color ${color}`}
                  onClick={() => setTweak('accent', color)}
                  style={{
                    width: 26,
                    height: 26,
                    borderRadius: 7,
                    background: color,
                    border:
                      tweaks.accent === color
                        ? '2px solid var(--text)'
                        : '2px solid transparent',
                    cursor: 'pointer',
                    padding: 0,
                  }}
                />
              ))}
            </div>
          </div>

          {/* Surface mood */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Surface mood
            </div>
            <div
              style={{
                display: 'flex',
                background: 'var(--bg-2)',
                borderRadius: 7,
                border: '1px solid var(--line)',
                padding: 2,
              }}
            >
              {MOOD_OPTIONS.map(({ value, label }) => (
                <button
                  key={value}
                  onClick={() => setTweak('mood', value)}
                  style={{
                    flex: 1,
                    padding: '5px 0',
                    fontSize: 11,
                    fontWeight: 500,
                    border: 0,
                    borderRadius: 5,
                    background: tweaks.mood === value ? 'var(--bg-3)' : 'transparent',
                    color: tweaks.mood === value ? 'var(--text)' : 'var(--muted)',
                    boxShadow:
                      tweaks.mood === value
                        ? 'inset 0 0 0 1px var(--line-2)'
                        : 'none',
                    cursor: 'pointer',
                  }}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {/* Canvas */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Canvas
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  color: 'var(--muted)',
                }}
              >
                Dot grid
                <ToggleButton
                  on={tweaks.grid}
                  onChange={(v) => setTweak('grid', v)}
                />
              </label>
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  color: 'var(--muted)',
                }}
              >
                Animated edges
                <ToggleButton
                  on={tweaks.flow}
                  onChange={(v) => setTweak('flow', v)}
                />
              </label>
            </div>
          </div>

          {/* Density */}
          <div>
            <div
              style={{
                fontSize: 10,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '.06em',
                color: 'var(--faint)',
                paddingTop: 8,
                marginBottom: 6,
              }}
            >
              Layout
            </div>
            <div
              style={{
                display: 'flex',
                background: 'var(--bg-2)',
                borderRadius: 7,
                border: '1px solid var(--line)',
                padding: 2,
              }}
            >
              {DENSITY_OPTIONS.map(({ value, label }) => (
                <button
                  key={value}
                  onClick={() => setTweak('density', value)}
                  style={{
                    flex: 1,
                    padding: '5px 0',
                    fontSize: 11,
                    fontWeight: 500,
                    border: 0,
                    borderRadius: 5,
                    background: tweaks.density === value ? 'var(--bg-3)' : 'transparent',
                    color: tweaks.density === value ? 'var(--text)' : 'var(--muted)',
                    boxShadow:
                      tweaks.density === value
                        ? 'inset 0 0 0 1px var(--line-2)'
                        : 'none',
                    cursor: 'pointer',
                  }}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function ToggleButton({ on, onChange }: { on: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      role="switch"
      aria-checked={on}
      onClick={() => onChange(!on)}
      style={{
        position: 'relative',
        width: 38,
        height: 22,
        borderRadius: 12,
        background: on ? 'var(--sampled)' : 'var(--bg-3)',
        border: on ? 'none' : '1px solid var(--line)',
        flex: '0 0 auto',
        transition: 'background .15s',
        cursor: 'pointer',
        padding: 0,
      }}
    >
      <span
        style={{
          position: 'absolute',
          top: 2,
          left: on ? undefined : 2,
          right: on ? 2 : undefined,
          width: 16,
          height: 16,
          borderRadius: '50%',
          background: '#fff',
          transition: 'all .15s',
        }}
      />
    </button>
  );
}
