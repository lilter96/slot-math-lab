import { parseModelJson } from '../lib/modelJson';
import { useQuery } from '@tanstack/react-query';
import { BASE_URL } from '../api/client';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { activateProject, captureProjectDraft, retainCurrentProject, useAppStore } from '../store';
import { peekRootProject, switchProject } from '../lib/projectFiles';
import { gameModelLibrary, starterLibrary } from '../lib/starterLibrary';
import { createNineLivesGraph } from '../games/nine-lives/graph';
import { getBrowserIdentity } from '../lib/browserIdentity';
import { parseEditableDraft } from '../lib/editableDraft';

const templates = [{ name: 'Nine Lives · reference', create: () => createNineLivesGraph() }, ...gameModelLibrary.map(model => ({ ...model, name: model.name === 'Nine Lives' ? 'Nine Lives · TGslots' : model.name })), ...starterLibrary].sort((a, b) => a.name.localeCompare(b.name));
export default function Projects() {
  const state = useAppStore();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [error, setError] = useState('');
  const [loadingId, setLoadingId] = useState<string | null>(null);
  const pending = useRef<AbortController | null>(null);
  const task = useRef(0), mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    const unsubscribe = useAppStore.subscribe((current, previous) => {
      if (current.projectId !== previous.projectId) { task.current++; pending.current?.abort(); pending.current = null; setLoadingId(null); }
    });
    return () => { mounted.current = false; task.current++; pending.current?.abort(); unsubscribe(); };
  }, []);
  const server = useQuery({ queryKey: ['configs'], queryFn: async ({ signal }) => {
    const response = await fetch(`${BASE_URL}/api/configs`, { signal });
    if (!response.ok) throw new Error(`Saved projects unavailable (HTTP ${response.status}).`);
    return await response.json() as { id: string; name: string; latestVersion: number }[];
  } });
  const projects = useMemo(() => {
    const entries = { ...state.projects };
    if (state.projectId) entries[state.projectId] = captureProjectDraft(state);
    return Object.entries(entries).filter(([, p]) => `${p.configName} ${p.tables.id ?? ''}`.toLowerCase().includes(search.toLowerCase()));
  }, [state, search]);
  const create = (config: Record<string, unknown>) => { task.current++; pending.current?.abort(); pending.current = null; switchProject(config); navigate('/overview'); };
  const identity = getBrowserIdentity();
  return <section className="projects-page" aria-labelledby="projects-heading">
    <header className="page-heading"><div><h1 id="projects-heading">My projects</h1><p>{identity.authenticated ? `Personal workspace · ${identity.userId}` : 'Local workspace · saved in this browser'}</p></div><button className="btn primary" onClick={() => create({ schemaVersion: '1.0.0', id: 'custom-model', name: 'Untitled model', nodes: [{ nodeType: 'metricsSink', id: 'metrics', label: 'Round payout', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [] })}>New blank project</button></header>
    <p className="hint">Your projects are private. Each template creates a separate editable copy. Browser drafts are saved automatically; use Export to download a backup or save a version to the server.</p>
    <label className="project-search">Find a project<input className="inp" type="search" placeholder="Name or model ID" value={search} onChange={e => setSearch(e.target.value)} /></label>
    <div className="project-list" aria-label="Personal projects">
      {projects.map(([id, project]) => <article className="project-row" key={id}>
        <div><h2>{String(project.graphTrail[0]?.graph.name ?? project.configName ?? 'Untitled')}</h2><p>{String(project.graphTrail[0]?.graph.id ?? project.tables.id ?? 'Custom model')} · {id === state.projectId ? 'Current project' : 'Browser draft'}</p></div>
        <button className="btn" onClick={() => { activateProject(id); navigate('/overview'); }}>Open project</button>
        <button className="btn" onClick={() => { activateProject(id); const config = peekRootProject(); if (config) create({ ...config, name: `${config.name ?? 'Untitled'} · copy` }); }}>Duplicate</button>
        <button className="btn" onClick={() => { retainCurrentProject(); const name = prompt('Project name', String(project.graphTrail[0]?.graph.name ?? project.configName ?? 'Untitled')); if (!name?.trim()) return; const saved = useAppStore.getState().projects[id]; const graphTrail = saved.graphTrail.map((entry, index) => index === 0 ? { ...entry, graph: { ...entry.graph, name: name.trim() } } : entry); const next = { ...saved, graphTrail, configName: saved.graphTrail.length ? saved.configName : name.trim() }; useAppStore.setState({ projects: { ...useAppStore.getState().projects, [id]: next }, ...(id === state.projectId ? { graphTrail, configName: next.configName } : {}) }); }}>Rename</button>
      </article>)}
      {!projects.length && <div className="projects-empty"><h2>{search ? 'No matching projects' : 'Start with a game or a blank model'}</h2><p>{search ? 'Try another name.' : 'Build several models side by side. Switching projects preserves your edits and open mechanic.'}</p></div>}
    </div>
    <section className="project-templates"><h2>Game and model templates</h2><p>Authored examples for exploration. Their RTP is not calibrated.</p><div className="template-list">{templates.map(template => <div className="template-row" key={template.name}><strong>{template.name}</strong><button className="btn" onClick={() => create(template.create())}>Create {template.name} project</button></div>)}</div></section>
    <section className="project-templates"><h2>Saved on the server</h2><p>Open a saved version as an editable browser draft. Existing saved versions and run evidence stay pinned.</p>
      {server.isPending && <p role="status">Loading saved projects…</p>}
      {server.isError && <p role="alert">{server.error.message} <button className="btn" onClick={() => void server.refetch()}>Retry saved projects</button></p>}
      <div className="project-list">{server.data?.filter(p => `${p.name} ${p.id}`.toLowerCase().includes(search.toLowerCase())).map(p => <div className="project-row" key={p.id}><div><h2>{p.name}</h2><p>Version {p.latestVersion} · {p.id}</p></div><button className="btn" disabled={!!loadingId} onClick={async () => {
        const token = ++task.current; pending.current?.abort(); const controller = new AbortController(); pending.current = controller; const current = () => mounted.current && token === task.current && !controller.signal.aborted; setLoadingId(p.id); setError('');
        try { const response = await fetch(`${BASE_URL}/api/configs/${encodeURIComponent(p.id)}`, { signal: controller.signal }); if (!response.ok) throw new Error(`Unable to open saved project (HTTP ${response.status}).`); const detail = parseModelJson(await response.text()); if (current()) create(detail.config as Record<string, unknown>); }
        catch (reason) { if (current()) setError(reason instanceof Error ? reason.message : 'Unable to open saved project'); }
        finally { if (current()) setLoadingId(null); }
      }}>{loadingId === p.id ? 'Opening…' : 'Open saved version'}</button></div>)}</div>
      {server.data?.length === 0 && <p className="hint">No server versions saved yet. Save a project from Export.</p>}
    </section>
    <section className="project-import"><h2>Import a personal model</h2><p className="hint">Model JSON or an editable-draft backup opens as a separate private project. Draft backups retain incomplete expressions for repair; saved run evidence is not restored.</p><label className="btn">Choose JSON file<input type="file" accept=".json,application/json" onChange={async e => { const file = e.target.files?.[0]; if (!file) return; e.target.value = ''; const token = ++task.current; pending.current?.abort(); pending.current = null; setLoadingId(null); setError(''); const current = () => mounted.current && token === task.current; try { const text = await file.text(); if (!current()) return; const config = parseModelJson(text); if (config.format === 'slotmath-editable-draft-v1') {
      const draft = parseEditableDraft(text); retainCurrentProject(); const id = crypto.randomUUID();
      useAppStore.setState({ projects: { ...useAppStore.getState().projects, [id]: draft } }); activateProject(id); navigate('/build');
    } else { if (!config.schemaVersion || !Array.isArray(config.nodes) || !Array.isArray(config.edges)) throw new Error('Choose a graph JSON or a slotmath-editable-draft-v1 backup.'); create(config); } } catch (err) { if (current()) setError(err instanceof Error ? err.message : 'Unable to import this file'); } }} /></label>{error && <p role="alert">{error}</p>}</section>
  </section>;
}
