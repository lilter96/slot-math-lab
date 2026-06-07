import { memo, type FC } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { GraphNodeData } from '../../../store';
import { Ic, NODE_ACCENT } from '../../Icons';

interface BaseNodeProps {
  data: GraphNodeData;
  selected: boolean;
}

const iconMap: Record<string, keyof typeof Ic> = {
  draw: 'draw',
  state: 'loop',
  loop: 'loop',
  branch: 'predicate',
  map: 'expr',
  evaluator: 'evaluator',
  transform: 'fit',
  sink: 'sink',
};

const BaseNodeCard: FC<BaseNodeProps> = ({ data, selected }) => {
  const accent = NODE_ACCENT[data.nodeType] ?? 'var(--text)';
  const iconKey = iconMap[data.nodeType] ?? 'draw';
  const IconC = Ic[iconKey];
  const lvlCls = data.level === 'b' ? 'node-lvl b' : data.level === 'c' ? 'node-lvl plugin' : 'node-lvl';
  const lvlTxt = data.level === 'b' ? 'expr' : data.level === 'c' ? 'plugin' : 'L0';
  const hasIn = data.nodeType !== 'draw';
  const hasOut = data.nodeType !== 'sink';

  return (
    <div className={'node' + (selected ? ' selected' : '')} style={{ '--sel': accent } as React.CSSProperties}>
      {hasIn && <Handle type="target" position={Position.Left} className="port in" />}
      <div className="node-head">
        <span className="node-ic" style={{ '--nc': accent } as React.CSSProperties}><IconC /></span>
        <div className="node-tt">
          <div className="node-title">{data.label}</div>
          {data.sub && <div className="node-sub">{data.sub}</div>}
        </div>
        <span className={lvlCls}>{lvlTxt}</span>
      </div>
      <div className="node-body">
        {data.nodeType === 'draw' && (() => {
          const dw = Array.isArray(data.drawWeights) ? data.drawWeights as { outcomeId: string; weight: number; value: number }[] : [];
          if (dw.length === 0) {
            return <div className="mini-row"><span className="k">mode</span><span className="v" style={{ color: 'var(--faint)' }}>unconfigured</span></div>;
          }
          const total = dw.reduce((s, w) => s + w.weight, 0);
          const exp = total > 0 ? dw.reduce((s, w) => s + w.value * w.weight, 0) / total : 0;
          return (
            <>
              <div className="mini-row"><span className="k">outcomes</span><span className="v">{dw.length}</span></div>
              <div className="mini-row"><span className="k">E[value]</span><span className="v" style={{ color: 'var(--exact)' }}>{exp.toFixed(2)}</span></div>
            </>
          );
        })()}
        {data.nodeType === 'loop' && (
          <>
            <div className="mini-row"><span className="k">iterations</span><span className="v">{data.iterations ?? '—'}</span></div>
            <div className="mini-row"><span className="k">termination</span><span className="v" style={{ color: data.terminationExpr ? 'var(--exact)' : 'var(--faint)' }}>{data.terminationExpr || 'none'}</span></div>
          </>
        )}
        {data.nodeType === 'evaluator' && (
          <div className="mini-row"><span className="k">kind</span><span className="v">{data.evaluatorKind ?? 'lines'}</span></div>
        )}
        {data.nodeType === 'branch' && data.expression && (
          <div className="expr-peek">{data.expression}</div>
        )}
        {data.nodeType === 'map' && data.expression && (
          <div className="expr-peek">{data.expression}</div>
        )}
        {data.nodeType === 'state' && (
          <div className="mini-row"><span className="k">state</span><span className="v">user-defined</span></div>
        )}
        {data.nodeType === 'transform' && (
          <div className="mini-row"><span className="k">transform</span><span className="v">{data.sub ?? 'ITransform'}</span></div>
        )}
        {data.nodeType === 'sink' && (
          <div className="mini-row"><span className="k">RTP</span><span className="v" style={{ color: 'var(--exact)', fontSize: 13 }}>—</span></div>
        )}
      </div>
      {hasOut && <Handle type="source" position={Position.Right} className="port out" />}
    </div>
  );
};

export default memo(BaseNodeCard);
