import { useCallback } from 'react';
import { Ic, NODE_ACCENT } from './Icons';

interface PaletteItem {
  id: string;
  label: string;
  sub: string;
  icon: keyof typeof Ic;
  accent: string;
  /** The @xyflow/react node type this creates */
  nodeType: string;
}

const PRIMITIVES: PaletteItem[] = [
  { id: 'draw', label: 'Draw', sub: 'Weighted choice', icon: 'draw', accent: NODE_ACCENT.draw, nodeType: 'draw' },
  { id: 'state', label: 'State', sub: 'Get / Put / Modify', icon: 'loop', accent: NODE_ACCENT.sink, nodeType: 'state' },
  { id: 'loop', label: 'Loop', sub: 'Fixpoint + stop', icon: 'loop', accent: NODE_ACCENT.loop, nodeType: 'loop' },
  { id: 'branch', label: 'Branch', sub: 'Bind + conditional', icon: 'predicate', accent: NODE_ACCENT.evaluator, nodeType: 'branch' },
  { id: 'map', label: 'Map', sub: 'Transform result', icon: 'expr', accent: NODE_ACCENT.expr, nodeType: 'map' },
];

const LIBRARY: PaletteItem[] = [
  { id: 'lines', label: 'Line Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'evaluator' },
  { id: 'ways', label: 'Ways Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'evaluator' },
  { id: 'cluster', label: 'Cluster Evaluator', sub: 'IEvaluator', icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'evaluator' },
  { id: 'scatter', label: 'Scatter', sub: 'IEvaluator', icon: 'target', accent: NODE_ACCENT.evaluator, nodeType: 'evaluator' },
  { id: 'reveal', label: 'Reveal', sub: 'ITransform', icon: 'draw', accent: NODE_ACCENT.draw, nodeType: 'transform' },
  { id: 'expand', label: 'Expand / Explode', sub: 'ITransform', icon: 'plus', accent: NODE_ACCENT.draw, nodeType: 'transform' },
  { id: 'lock', label: 'Lock / Sticky', sub: 'ITransform', icon: 'check', accent: NODE_ACCENT.expr, nodeType: 'transform' },
  { id: 'morph', label: 'Morph / Upgrade', sub: 'ITransform', icon: 'loop', accent: NODE_ACCENT.loop, nodeType: 'transform' },
  { id: 'tumble', label: 'Refill / Tumble', sub: 'ITransform', icon: 'fit', accent: NODE_ACCENT.sink, nodeType: 'transform' },
  // Sink node
  { id: 'sink', label: 'Metrics Sink', sub: 'Output', icon: 'sink', accent: NODE_ACCENT.sink, nodeType: 'sink' },
];

function PaletteRow({ item }: { item: PaletteItem }) {
  const Icon = Ic[item.icon];
  const onDragStart = useCallback((e: React.DragEvent) => {
    e.dataTransfer.setData('application/slotnode', item.nodeType);
    e.dataTransfer.effectAllowed = 'move';
  }, [item.nodeType]);

  return (
    <div
      className="pal-item"
      title={`${item.label} — ${item.sub}`}
      draggable
      onDragStart={onDragStart}
    >
      <div className="pic" style={{ color: item.accent }}>
        <Icon />
      </div>
      <div>
        <div className="pt">{item.label}</div>
        <div className="ps">{item.sub}</div>
      </div>
    </div>
  );
}

export default function Palette() {
  return (
    <div className="palette" tabIndex={0}>
      <div className="grp">
        <div className="section-label">Primitives</div>
      </div>
      {PRIMITIVES.map((item) => (
        <PaletteRow key={item.id} item={item} />
      ))}
      <div className="grp">
        <div className="section-label">Library</div>
      </div>
      {LIBRARY.map((item) => (
        <PaletteRow key={item.id} item={item} />
      ))}
    </div>
  );
}
