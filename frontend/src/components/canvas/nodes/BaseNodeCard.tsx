import { drawCardSummary } from '../../../lib/drawRows';
import { memo, type FC } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { GraphNodeData } from '../../../store';
import { useAppStore } from '../../../store';
import { Ic, NODE_ACCENT } from '../../Icons';
import ProvBadge from '../../ProvBadge';
import type { Provenance } from '../../ProvBadge';
import { openMechanic } from '../../../lib/projectFiles';
import { dataPorts } from '../../../lib/graphConnections';

interface BaseNodeProps {
  id: string;
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
  library: 'evaluator',
  sink: 'sink',
};

const BaseNodeCard: FC<BaseNodeProps> = ({ id, data, selected }) => {
  const accent = NODE_ACCENT[data.nodeType] ?? 'var(--text)';
  const iconKey = iconMap[data.nodeType] ?? 'draw';
  const IconC = Ic[iconKey];
  const lvlCls = data.level === 'b' ? 'node-lvl b' : data.level === 'c' ? 'node-lvl plugin' : 'node-lvl';
  const lvlTxt = data.level === 'b' ? 'expr' : data.level === 'c' ? 'plugin' : 'L0';
  const inputPorts = dataPorts(id, data, 'inputs');
  const outputPorts = dataPorts(id, data, 'outputs');
  const liveRtp = useAppStore((s) => s.liveRtp);
  const liveProvenance = useAppStore((s) => s.liveProvenance);
  const canOpen = useAppStore(s => !!(s.tables.mechanics as Record<string, unknown>)?.[String(data.mechanicName)]);
  const rtpProvenance: Provenance | null = liveRtp == null ? null : liveProvenance === 'Exact'
    ? { kind: 'Exact' }
    : liveProvenance === 'ExactInterval' ? { kind: 'ExactInterval' }
    : liveProvenance === 'ExactWithMassLoss' ? { kind: 'ExactWithMassLoss' }
    : liveProvenance === 'Sampled'
      ? { kind: 'Sampled' }
      : null;

  return (
    <div className={'node' + (selected ? ' selected' : '')} style={{ '--sel': accent } as React.CSSProperties}>
      {inputPorts.map((port, index) => <Handle key={port.id} id={port.id} type="target" position={Position.Left} className="port in" title={`${port.id}: ${port.type}`} style={{ top: `${(index + 1) * 100 / (inputPorts.length + 1)}%` }} />)}
      <div className="node-head">
        <span className="node-ic" style={{ '--nc': accent } as React.CSSProperties}><IconC /></span>
        <div className="node-tt">
          <div className="node-title" title={data.label}>{data.label}</div>
          {data.sub && <div className="node-sub">{data.sub}</div>}
        </div>
        <span className={lvlCls}>{lvlTxt}</span>
      </div>
      <div className="node-body">
        {data.nodeType === 'draw' && (() => {
          const dw = Array.isArray(data.drawWeights) ? data.drawWeights as { outcomeId: string; weight: number; value: number }[] : [];
          const summary = drawCardSummary(data);
          return (
            <>
              <div className="mini-row"><span className="k">outcomes</span><span className="v">{dw.length}</span></div>
              <div className="mini-row" data-testid="draw-source-summary"><span className="k">{summary.label}</span><span className="v" title={summary.value} style={{ minWidth: 0, whiteSpace: 'normal', overflowWrap: 'anywhere', textAlign: 'right' }}>{summary.value.includes('/') ? <>{summary.value.split('/')[0]}/<wbr />{summary.value.split('/')[1]}</> : summary.value}</span></div>
              {data.stateWriteKey && (
                <div className="mini-row">
                  <span className="k">→ state</span>
                  <span className="v" style={{ color: 'var(--sampled)' }}>{data.stateWriteKey as string}</span>
                </div>
              )}
            </>
          );
        })()}
        {data.nodeType === 'loop' && (
          <>
            <div className="mini-row"><span className="k">iterations</span><span className="v">{data.iterations ?? '—'}</span></div>
            <div className="mini-row"><span className="k">stop</span><span className="v" style={{ color: data.terminationExpr ? 'var(--exact)' : 'var(--faint)' }}>{(data.terminationExpr as string) || 'none'}</span></div>
          </>
        )}
        {data.nodeType === 'evaluator' && (
          <div className="mini-row"><span className="k">kind</span><span className="v">{data.evaluatorKind ?? 'lines'}</span></div>
        )}
        {data.nodeType === 'branch' && (
          <div className="mini-row">
            <span className="k">condition</span>
            <span className="v" style={{ color: data.expression ? 'var(--exact)' : 'var(--faint)' }}>
              {(data.expression as string) || 'none'}
            </span>
          </div>
        )}
        {data.nodeType === 'map' && data.expression && (
          <div className="expr-peek">{data.expression}</div>
        )}
        {data.nodeType === 'state' && (
          <>
            <div className="mini-row"><span className="k">op</span><span className="v">{(data.stateOp as string) ?? 'get'}</span></div>
            {data.stateKey && <div className="mini-row"><span className="k">key</span><span className="v">{data.stateKey as string}</span></div>}
          </>
        )}
        {data.nodeType === 'transform' && (
          <div className="mini-row"><span className="k">transform</span><span className="v">{data.sub ?? 'ITransform'}</span></div>
        )}
        {data.nodeType === 'library' && (
          <div className="mini-row">
            <span className="k">mechanic</span>
            <span className="v" style={{ color: 'var(--exact)' }}>{(data.mechanicName as string) ?? '—'}</span>
          </div>
        )}
        {data.nodeType === 'library' && canOpen && <button className="btn nodrag nopan node-open-subgraph" aria-label={`Open ${data.label} subgraph`} onClick={event => { event.stopPropagation(); openMechanic(String(data.mechanicName)); }}>Open subgraph ↗</button>}
        {data.nodeType === 'sink' && (
          <div className="mini-row">
            <span className="k">RTP</span>
            <span className="v" style={{ color: liveRtp != null ? liveProvenance === 'Sampled' ? 'var(--sampled)' : 'var(--text)' : 'var(--faint)', fontSize: 13 }}>
              {liveRtp != null ? `${(liveRtp * 100).toFixed(2)}%` : '—'}
            </span>
            {rtpProvenance && <ProvBadge p={rtpProvenance} mini />}
          </div>
        )}
      </div>
      {outputPorts.map((port, index) => <Handle key={port.id} id={port.id} type="source" position={Position.Right} className="port out" title={`${port.id}: ${port.type}`} style={{ top: `${(index + 1) * 100 / (outputPorts.length + 1)}%` }} />)}
    </div>
  );
};

export default memo(BaseNodeCard);
