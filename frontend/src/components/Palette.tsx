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
  /** Extra data to include in dataTransfer (e.g. mechanicName for library nodes) */
  extra?: Record<string, string>;
}

const PRIMITIVES: PaletteItem[] = [
  { id: 'draw',   label: 'Draw',   sub: 'Weighted choice',    icon: 'draw',      accent: NODE_ACCENT.draw,      nodeType: 'draw' },
  { id: 'state',  label: 'State',  sub: 'Get / Put / Modify', icon: 'loop',      accent: NODE_ACCENT.sink,      nodeType: 'state' },
  { id: 'loop',   label: 'Loop',   sub: 'Fixpoint + stop',    icon: 'loop',      accent: NODE_ACCENT.loop,      nodeType: 'loop' },
  { id: 'branch', label: 'Branch', sub: 'Bind + conditional', icon: 'predicate', accent: NODE_ACCENT.evaluator, nodeType: 'branch' },
  { id: 'map',    label: 'Map',    sub: 'Transform result',   icon: 'expr',      accent: NODE_ACCENT.expr,      nodeType: 'map' },
];

/** Standard catalog mechanics (G10). Each maps to a LibraryNode with mechanicName. */
const CATALOG: PaletteItem[] = [
  { id: 'lib-scatter',      label: 'Scatter',     sub: 'Catalog — pays anywhere',      icon: 'target',    accent: NODE_ACCENT.evaluator, nodeType: 'library', extra: { mechanicName: 'scatter' } },
  { id: 'lib-lines',        label: 'Lines',       sub: 'Catalog — payline scan',        icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'library', extra: { mechanicName: 'lines' } },
  { id: 'lib-ways',         label: 'Ways',        sub: 'Catalog — 243-ways',            icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'library', extra: { mechanicName: 'ways' } },
  { id: 'lib-cascade',      label: 'Cascade',     sub: 'Catalog — tumble / refill',     icon: 'fit',       accent: NODE_ACCENT.loop,      nodeType: 'library', extra: { mechanicName: 'cascade' } },
  { id: 'lib-sticky-wild',  label: 'Sticky Wild', sub: 'Catalog — lock wild cells',     icon: 'check',     accent: NODE_ACCENT.expr,      nodeType: 'library', extra: { mechanicName: 'sticky-wild' } },
  { id: 'lib-hold-and-win', label: 'Hold & Win',  sub: 'Catalog — collect & respin',    icon: 'sink',      accent: NODE_ACCENT.sink,      nodeType: 'library', extra: { mechanicName: 'hold-and-win' } },
];

const SINKS_AND_PLUGINS: PaletteItem[] = [
  { id: 'evaluator', label: 'Evaluator',    sub: 'Custom IEvaluator',  icon: 'evaluator', accent: NODE_ACCENT.evaluator, nodeType: 'evaluator' },
  { id: 'transform', label: 'Transform',    sub: 'Custom ITransform',  icon: 'draw',      accent: NODE_ACCENT.draw,      nodeType: 'transform' },
  { id: 'sink',      label: 'Metrics Sink', sub: 'Output',             icon: 'sink',      accent: NODE_ACCENT.sink,      nodeType: 'sink' },
];

function PaletteRow({ item }: { item: PaletteItem }) {
  const Icon = Ic[item.icon];
  const onDragStart = useCallback(
    (e: React.DragEvent) => {
      e.dataTransfer.setData('application/slotnode', item.nodeType);
      if (item.extra) {
        e.dataTransfer.setData('application/slotnodeextra', JSON.stringify(item.extra));
      }
      e.dataTransfer.effectAllowed = 'move';
    },
    [item.nodeType, item.extra],
  );

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
        <div className="section-label">Standard Catalog</div>
      </div>
      {CATALOG.map((item) => (
        <PaletteRow key={item.id} item={item} />
      ))}

      <div className="grp">
        <div className="section-label">Custom / Plugin</div>
      </div>
      {SINKS_AND_PLUGINS.map((item) => (
        <PaletteRow key={item.id} item={item} />
      ))}
    </div>
  );
}
