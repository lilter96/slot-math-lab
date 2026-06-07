import { Ic, NODE_ACCENT } from './Icons';

interface PaletteItem {
  id: string;
  label: string;
  sub: string;
  icon: keyof typeof Ic;
  accent: string;
}

const PRIMITIVES: PaletteItem[] = [
  { id: 'draw', label: 'Draw', sub: 'Weighted choice', icon: 'draw', accent: NODE_ACCENT.draw },
  { id: 'state', label: 'State', sub: 'Get / Put / Modify', icon: 'loop', accent: NODE_ACCENT.sink },
  { id: 'loop', label: 'Loop', sub: 'Fixpoint + stop', icon: 'loop', accent: NODE_ACCENT.loop },
  { id: 'branch', label: 'Branch', sub: 'Bind + conditional', icon: 'predicate', accent: NODE_ACCENT.evaluator },
  { id: 'map', label: 'Map', sub: 'Transform result', icon: 'expr', accent: NODE_ACCENT.expr },
];

const LIBRARY: PaletteItem[] = [
  { id: 'lines', label: 'Line Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'ways', label: 'Ways Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'cluster', label: 'Cluster Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator },
  { id: 'scatter', label: 'Scatter', sub: 'IEvaluator', icon: 'target', accent: NODE_ACCENT.evaluator },
  { id: 'reveal', label: 'Reveal', sub: 'ITransform', icon: 'draw', accent: NODE_ACCENT.draw },
  { id: 'expand', label: 'Expand / Explode', sub: 'ITransform', icon: 'plus', accent: NODE_ACCENT.draw },
  { id: 'lock', label: 'Lock / Sticky', sub: 'ITransform', icon: 'check', accent: NODE_ACCENT.expr },
  { id: 'morph', label: 'Morph / Upgrade', sub: 'ITransform', icon: 'loop', accent: NODE_ACCENT.loop },
  { id: 'tumble', label: 'Refill / Tumble', sub: 'ITransform', icon: 'fit', accent: NODE_ACCENT.sink },
];

export default function Palette() {
  return (
    <div className="palette" tabIndex={0}>
      <div className="grp">
        <div className="section-label">Primitives</div>
      </div>
      {PRIMITIVES.map((item) => {
        const Icon = Ic[item.icon];
        return (
          <div key={item.id} className="pal-item" title={`${item.label} — ${item.sub}`}>
            <div className="pic" style={{ color: item.accent }}>
              <Icon />
            </div>
            <div>
              <div className="pt">{item.label}</div>
              <div className="ps">{item.sub}</div>
            </div>
          </div>
        );
      })}
      <div className="grp">
        <div className="section-label">Library</div>
      </div>
      {LIBRARY.map((item) => {
        const Icon = Ic[item.icon];
        return (
          <div key={item.id} className="pal-item" title={`${item.label} — ${item.sub}`}>
            <div className="pic" style={{ color: item.accent }}>
              <Icon />
            </div>
            <div>
              <div className="pt">{item.label}</div>
              <div className="ps">{item.sub}</div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
