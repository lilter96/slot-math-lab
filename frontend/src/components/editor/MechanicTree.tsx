import { mechanicLabel, referencedMechanics, type ModelGraph } from '../../lib/modelProjection';
import { rootProject, openMechanic } from '../../lib/projectFiles';
export default function MechanicTree({ graph, search }: { graph: ModelGraph; search: string }) {
  const definitions = graph.mechanics ?? {};
  const query = search.toLowerCase().replaceAll('-', ' ');
  const open = (id: string) => { rootProject(); openMechanic(id); };
  const button = (id: string) => <button className="mechanic-link" aria-label={`Open ${id} graph`} title={definitions[id]?.description} onClick={() => open(id)}><span>{mechanicLabel(id)}</span><small>{id} · {definitions[id]?.nodes.length ?? 0} nodes</small></button>;
  const render = (id: string, path: string[], depth = 0): React.ReactNode => {
    if (!definitions[id]) return null;
    const children = path.includes(id) ? [] : referencedMechanics(definitions[id].nodes).filter(child => !!definitions[child]);
    return <li key={id}>{children.length ? <details open={depth === 0}><summary>{mechanicLabel(id)}</summary>{button(id)}<ul>{children.map(child => render(child, [...path, id], depth + 1))}</ul></details> : button(id)}</li>;
  };
  const roots = referencedMechanics(graph.nodes).filter(id => !!definitions[id]);
  const referenced = new Set(Object.values(definitions).flatMap(definition => referencedMechanics(definition.nodes)));
  const other = Object.keys(definitions).filter(id => !roots.includes(id) && !referenced.has(id));
  if (search) {
    const matches = Object.entries(definitions).filter(([id, definition]) => `${mechanicLabel(id)} ${id} ${definition.description ?? ''}`.toLowerCase().replaceAll('-', ' ').includes(query));
    return <ul className="mechanic-tree">{matches.map(([id]) => <li key={id}>{button(id)}</li>)}{!matches.length && <li className="hint">No matching mechanic. Try its name or ID.</li>}</ul>;
  }
  return <ul className="mechanic-tree">{[...roots, ...other].map(id => render(id, []))}{!Object.keys(definitions).length && <li className="hint">No named mechanics. Edit the full graph or extract selected nodes.</li>}<li><details><summary>All definitions ({Object.keys(definitions).length})</summary>{Object.keys(definitions).map(id => <div key={id}>{button(id)}</div>)}</details></li></ul>;
}
