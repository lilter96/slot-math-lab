import { useState } from 'react';
import { useAppStore } from '../../store';
import { connectionError, nodePorts } from '../../lib/graphConnections';
import GraphDialog from './GraphDialog';

export default function ConnectNodesDialog({ onClose }: { onClose(): void }) {
  const nodes = useAppStore(state => state.nodes), edges = useAppStore(state => state.edges);
  const sources = nodes.filter(node => nodePorts(node, 'outputs').length), targets = nodes.filter(node => nodePorts(node, 'inputs').length);
  const [sourceId, setSource] = useState(() => sources.find(node => node.id === useAppStore.getState().selectedNodeId)?.id ?? sources[0]?.id ?? '');
  const [targetId, setTarget] = useState(() => targets.find(node => node.id !== sourceId)?.id ?? targets[0]?.id ?? '');
  const source = nodes.find(node => node.id === sourceId), target = nodes.find(node => node.id === targetId);
  const output = source ? nodePorts(source, 'outputs') : [], input = target ? nodePorts(target, 'inputs') : [];
  const [sourcePort, setSourcePort] = useState(() => output[0]?.id ?? ''), [targetPort, setTargetPort] = useState(() => input[0]?.id ?? '');
  const [result, setResult] = useState('');
  const candidate = { source: sourceId, target: targetId, sourceHandle: sourcePort, targetHandle: targetPort };
  const invalid = connectionError(nodes, edges, candidate);
  return <GraphDialog title="Connect graph nodes" onClose={onClose}>
    <p>Choose declared output and input ports. The same checks apply to pointer connections; compiler validation still checks the complete graph.</p>
    <form onSubmit={event => {
      event.preventDefault(); setResult('');
      if (invalid) return;
      const state = useAppStore.getState(), count = state.edges.length;
      state.onConnect(candidate);
      const current = useAppStore.getState();
      setResult(current.edges.length === count + 1 ? 'Connection added to this graph.' : current.edgeValidationError ?? current.limitError ?? 'Connection was not added. Review the current graph.');
    }}>
      <div className="field"><label htmlFor="connection-source">Source node</label><select id="connection-source" className="inp" value={sourceId} onChange={event => { setSource(event.target.value); setSourcePort(nodePorts(nodes.find(node => node.id === event.target.value)!, 'outputs')[0]?.id ?? ''); setResult(''); }}>{sources.map(node => <option key={node.id} value={node.id}>{node.data.label} · {node.id}</option>)}</select></div>
      <div className="field"><label htmlFor="connection-output">Output port</label><select id="connection-output" className="inp" value={sourcePort} onChange={event => { setSourcePort(event.target.value); setResult(''); }}>{output.map(port => <option key={port.id} value={port.id}>{port.id} · {port.type}</option>)}</select></div>
      <div className="field"><label htmlFor="connection-target">Target node</label><select id="connection-target" className="inp" value={targetId} onChange={event => { setTarget(event.target.value); setTargetPort(nodePorts(nodes.find(node => node.id === event.target.value)!, 'inputs')[0]?.id ?? ''); setResult(''); }}>{targets.map(node => <option key={node.id} value={node.id}>{node.data.label} · {node.id}</option>)}</select></div>
      <div className="field"><label htmlFor="connection-input">Input port</label><select id="connection-input" className="inp" value={targetPort} onChange={event => { setTargetPort(event.target.value); setResult(''); }}>{input.map(port => <option key={port.id} value={port.id}>{port.id} · {port.type}</option>)}</select></div>
      {invalid && result !== 'Connection added to this graph.' && <p role="alert" className="inline-validation">{invalid}</p>}
      {result && <p role="status">{result}</p>}
      <button className="btn primary" type="submit" disabled={!!invalid}>Connect ports</button>
    </form>
  </GraphDialog>;
}
