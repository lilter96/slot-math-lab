import { memo, type FC } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { GraphNodeData } from '../../../store';
import { useAppStore } from '../../../store';
import { Ic, NODE_ACCENT } from '../../Icons';
import ProvBadge from '../../ProvBadge';
import type { Provenance } from '../../ProvBadge';
import { openMechanic } from '../../../lib/projectFiles';

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
  library: 'evaluator',
  sink: 'sink',
};

const BaseNodeCard: FC<BaseNodeProps> = ({ data, selected }) => {
  const accent = NODE_ACCENT[data.nodeType] ?? 'var(--text)';
  const iconKey = iconMap[data.nodeType] ?? 'draw';
  const IconC = Ic[iconKey];
  const lvlCls = data.level === 'b' ? 'node-lvl b' : data.level === 'c' ? 'node-lvl plugin' : 'node-lvl';
  const lvlTxt = data.level === 'b' ? 'expr' : data.level === 'c' ? 'plugin' : 'L0';
  // Draw nodes generate values — they have no input handles
  const hasIn = data.nodeType !== 'draw' || !!(data.backendNode as { inputs?: object } | undefined)?.inputs;
  const hasOut = data.nodeType !== 'sink' && data.nodeType !== 'branch' && data.nodeType !== 'loop';
  const backendNode = data.backendNode as { inputs?: Record<string, unknown>; outputs?: Record<string, unknown> } | undefined;
  const inputPorts = Object.keys(backendNode?.inputs ?? {});
  const outputPorts = Object.keys(backendNode?.outputs ?? {});
  const liveRtp = useAppStore((s) => s.liveRtp);
  const liveProvenance = useAppStore((s) => s.liveProvenance);
  const canOpen = useAppStore(s => !!(s.tables.mechanics as Record<string, unknown>)?.[String(data.mechanicName)]);
  const rtpProvenance: Provenance | null = liveProvenance === 'Exact'
    ? { kind: 'Exact' }
    : liveProvenance === 'ExactInterval' ? { kind: 'ExactInterval' }
    : liveProvenance === 'ExactWithMassLoss' ? { kind: 'ExactWithMassLoss' }
    : liveProvenance === 'Sampled'
      ? { kind: 'Sampled', n: 0 }
      : null;

  return (
    <div className={'node' + (selected ? ' selected' : '')} style={{ '--sel': accent } as React.CSSProperties}>
      {hasIn && (inputPorts.length ? inputPorts.map((id, index) => <Handle key={id} id={id} type="target" position={Position.Left} className="port in" style={{ top: `${(index + 1) * 100 / (inputPorts.length + 1)}%` }} />) : <Handle type="target" position={Position.Left} className="port in" />)}
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
          if (dw.length === 0) {
            return <div className="mini-row"><span className="k">mode</span><span className="v" style={{ color: 'var(--faint)' }}>unconfigured</span></div>;
          }
          const total = dw.reduce((s, w) => s + w.weight, 0);
          const exp = total > 0 ? dw.reduce((s, w) => s + w.value * w.weight, 0) / total : 0;
          return (
            <>
              <div className="mini-row"><span className="k">outcomes</span><span className="v">{dw.length}</span></div>
              <div className="mini-row"><span className="k">E[value]</span><span className="v" style={{ color: 'var(--exact)' }}>{exp.toFixed(2)}</span></div>
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
            <span className="v" style={{ color: liveRtp != null ? 'var(--exact)' : 'var(--faint)', fontSize: 13 }}>
              {liveRtp != null ? `${(liveRtp * 100).toFixed(2)}%` : '—'}
            </span>
            {rtpProvenance && <ProvBadge p={rtpProvenance} mini />}
          </div>
        )}
      </div>
      {hasOut && (outputPorts.length ? outputPorts.map((id, index) => <Handle key={id} id={id} type="source" position={Position.Right} className="port out" style={{ top: `${(index + 1) * 100 / (outputPorts.length + 1)}%` }} />) : <Handle type="source" position={Position.Right} className="port out" />)}
      {data.nodeType === 'branch' && (
        <>
          <Handle id="true" type="source" position={Position.Right}
            style={{ top: '33%' }} className="port out" />
          <Handle id="false" type="source" position={Position.Right}
            style={{ top: '67%' }} className="port out" />
        </>
      )}
      {data.nodeType === 'loop' && (
        <>
          <Handle id="body" type="source" position={Position.Right}
            style={{ top: '35%' }} className="port out" />
          <Handle id="exit" type="source" position={Position.Right}
            style={{ top: '65%' }} className="port out" />
        </>
      )}
    </div>
  );
};

export default memo(BaseNodeCard);
