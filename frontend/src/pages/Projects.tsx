import { parseModelJson } from '../lib/modelJson';
import { useQuery } from '@tanstack/react-query';
import { BASE_URL } from '../api/client';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { activateProject, captureProjectDraft, retainCurrentProject, useAppStore, type ProjectDraft } from '../store';
import { switchProject } from '../lib/projectFiles';
import { gameModelLibrary, starterLibrary } from '../lib/starterLibrary';
import { createNineLivesGraph } from '../games/nine-lives/graph';
import { getBrowserIdentity } from '../lib/browserIdentity';
import { parseEditableDraft } from '../lib/editableDraft';
import { Ic } from '../components/Icons';
import './Projects.css';

const templates = [
  { name: 'Nine Lives · reference', summary: 'Reference graph for exploring reusable mechanics.', kind: 'Reference model', create: () => createNineLivesGraph() },
  ...gameModelLibrary.map(model => ({ ...model, name: model.name === 'Nine Lives' ? 'Nine Lives · TGslots' : model.name, summary: 'Explore the authored game graph and its reusable mechanics.', kind: 'Game model' })),
  ...starterLibrary.map(model => ({ ...model, kind: 'Starter model' })),
].sort((a, b) => a.name.localeCompare(b.name));
const projectName = (project: ProjectDraft) => String(project.graphTrail[0]?.graph.name ?? project.configName ?? 'Untitled');
const blank = () => ({ schemaVersion: '1.0.0', id: 'custom-model', name: 'Untitled model', nodes: [{ nodeType: 'metricsSink', id: 'metrics', label: 'Round payout', winCap: 10000, inputs: { in: { name: 'in', type: 'Wins' } } }], edges: [] });

export default function Projects() {
  const state = useAppStore(), navigate = useNavigate();
  const [search, setSearch] = useState(''), [error, setError] = useState('');
  const [errorArea, setErrorArea] = useState<'library' | 'import' | 'server'>('import');
  const [loadingId, setLoadingId] = useState<string | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null), [name, setName] = useState('');
  const pending = useRef<AbortController | null>(null);
  const operation = useRef({ token: 0, mounted: false });
  useEffect(() => {
    const lifecycle = operation.current; lifecycle.mounted = true;
    const unsubscribe = useAppStore.subscribe((current, previous) => {
      if (current.projectId !== previous.projectId) { lifecycle.token++; pending.current?.abort(); pending.current = null; setLoadingId(null); }
    });
    return () => { lifecycle.mounted = false; lifecycle.token++; pending.current?.abort(); unsubscribe(); };
  }, []);
  const server = useQuery({ queryKey: ['configs'], queryFn: async ({ signal }) => {
    const response = await fetch(`${BASE_URL}/api/configs`, { signal });
    if (!response.ok) throw new Error(`Saved projects unavailable (HTTP ${response.status}).`);
    const data: unknown = await response.json();
    if (!Array.isArray(data) || data.some(p => !p || typeof p.id !== 'string' || typeof p.name !== 'string' || !Number.isSafeInteger(p.latestVersion))) throw new Error('The server returned an invalid project archive.');
    return data as { id: string; name: string; latestVersion: number }[];
  } });
  const collection = useMemo(() => {
    const entries = { ...state.projects };
    if (state.projectId) entries[state.projectId] = captureProjectDraft(state);
    return Object.entries(entries);
  }, [state]);
  const projects = collection.filter(([, p]) => `${projectName(p)} ${p.graphTrail[0]?.graph.id ?? p.tables.id ?? ''}`.toLowerCase().includes(search.toLowerCase()));
  const saved = server.data?.filter(p => `${p.name} ${p.id}`.toLowerCase().includes(search.toLowerCase())) ?? [];
  const invalidate = () => { operation.current.token++; pending.current?.abort(); pending.current = null; setLoadingId(null); };
  const create = (config: Record<string, unknown>) => { invalidate(); switchProject(config); navigate('/overview'); };
  const openDraft = (draft: ProjectDraft) => {
    invalidate(); retainCurrentProject(); const id = crypto.randomUUID();
    useAppStore.setState({ projects: { ...useAppStore.getState().projects, [id]: draft } }); activateProject(id); navigate('/build');
  };
  const rename = (id: string) => {
    if (!name.trim()) return;
    retainCurrentProject(); const project = useAppStore.getState().projects[id]; if (!project) return;
    const graphTrail = project.graphTrail.map((entry, index) => index === 0 ? { ...entry, graph: { ...entry.graph, name: name.trim() } } : entry);
    const next = { ...project, graphTrail, configName: graphTrail.length ? project.configName : name.trim() };
    useAppStore.setState({ projects: { ...useAppStore.getState().projects, [id]: next }, ...(id === state.projectId ? { graphTrail, configName: next.configName } : {}) }); setRenaming(null);
  };
  const identity = getBrowserIdentity();
  return <section className="projects-page" aria-labelledby="projects-heading">
    <header className="projects-heading"><div><p className="projects-context">{identity.authenticated ? `Private workspace · ${identity.userId}` : 'Private workspace · this browser'}</p><h1 id="projects-heading">My projects</h1><p>Build, explore and refine your game models.</p></div><button className="btn primary" onClick={() => create(blank())}>New blank project</button></header>
    <div className="projects-layout"><section className="projects-library" aria-labelledby="library-heading">
      <div className="projects-library-heading"><div><h2 id="library-heading">Your library <span>{collection.length}</span></h2><p>Independent drafts. Each project keeps its own editing context.</p></div><label className="project-search">Find a project<input className="inp" type="search" placeholder="Name or model ID" value={search} onChange={e => setSearch(e.target.value)} /></label></div>
      <div className="project-list" aria-label="Personal projects">
        {projects.map(([id, project]) => <article className={`project-row ${id === state.projectId ? 'current' : ''}`} key={id}>
          <div className="project-card-heading"><span className="project-glyph"><Ic.build aria-hidden="true" /></span><span className="project-state">{id === state.projectId ? 'Current project' : 'Browser draft'}</span></div>
          <div className="project-card-content"><h2>{projectName(project)}</h2><p>{String(project.graphTrail[0]?.graph.id ?? project.tables.id ?? 'Custom model')}</p><dl><div><dt>Open graph</dt><dd>{project.nodes.length} {project.nodes.length === 1 ? 'node' : 'nodes'}</dd></div><div><dt>Editing context</dt><dd>{project.graphTrail.at(-1)?.mechanic ?? 'Root model'}</dd></div></dl></div>
          {renaming === id ? <form className="project-rename" onSubmit={event => { event.preventDefault(); rename(id); }}><label>Project name<input className="inp" autoFocus value={name} onChange={event => setName(event.target.value)} onKeyDown={event => { if (event.key === 'Escape') setRenaming(null); }} /></label><div><button className="btn" type="submit" disabled={!name.trim()}>Save name</button><button className="btn" type="button" onClick={() => setRenaming(null)}>Cancel rename</button></div></form> : <footer className="project-card-actions"><button className="btn project-open" onClick={() => { invalidate(); activateProject(id); navigate('/overview'); }}>Open project</button><button className="btn" onClick={() => {
            setError(''); setErrorArea('library');
            try { const draft = parseEditableDraft(JSON.stringify({ format: 'slotmath-editable-draft-v1', ...project })); const title = `${projectName(project)} · copy`; draft.configName = draft.graphTrail.length ? draft.configName : title; if (draft.graphTrail.length) draft.graphTrail[0].graph.name = title; openDraft(draft); }
            catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to duplicate this draft.'); }
          }}>Duplicate</button><button className="btn" onClick={() => { setRenaming(id); setName(projectName(project)); }}>Rename</button></footer>}
        </article>)}
        {!projects.length && <div className="projects-empty"><span className="projects-empty-glyph"><Ic.build aria-hidden="true" /></span><h2>{search ? 'No matching projects' : 'Start with a game or a blank model'}</h2><p>{search ? 'Try another name or clear the search.' : 'Create your first model, start from an authored example or import your own JSON. New projects remain independent of their templates.'}</p>{search ? <button className="btn" onClick={() => setSearch('')}>Clear search</button> : <a className="btn" href="#project-template-catalog">Explore templates</a>}</div>}
      </div>{error && errorArea === 'library' && <div className="projects-error" role="alert"><strong>Project could not be opened</strong><p>{error}</p><button className="btn" onClick={() => setError('')}>Dismiss error</button></div>}
    </section><aside className="projects-side" aria-label="Project import and storage">
      <section className="project-import"><span className="project-import-icon"><Ic.build aria-hidden="true" /></span><h2>Import a personal model</h2><p>Bring your own graph or restore an editable-draft backup. It opens as a new private project.</p><label className="btn project-file-choice">Choose JSON file<input type="file" aria-label="Choose JSON file" accept=".json,application/json" onChange={async e => {
        const file = e.target.files?.[0]; if (!file) return; setErrorArea('import'); e.target.value = ''; invalidate(); const token = operation.current.token; setError('');
        const current = () => operation.current.mounted && token === operation.current.token;
        try { const text = await file.text(); if (!current()) return; const config = parseModelJson(text); if (config.format === 'slotmath-editable-draft-v1') openDraft(parseEditableDraft(text));
          else { if (!config.schemaVersion || !Array.isArray(config.nodes) || !Array.isArray(config.edges)) throw new Error('Choose a graph JSON or a slotmath-editable-draft-v1 backup.'); create(config); } }
        catch (reason) { if (current()) setError(reason instanceof Error ? reason.message : 'Unable to import this file.'); }
      }} /></label><small>JSON models and editable backups. Saved run evidence is not imported.</small>{error && errorArea === 'import' && <div className="projects-error" role="alert"><strong>Project could not be opened</strong><p>{error}</p><button className="btn" onClick={() => setError('')}>Dismiss error</button></div>}</section>
      <section className="projects-storage"><h2>Your work stays private</h2><dl><div><dt>Browser drafts</dt><dd>Saved locally as you edit.</dd></div><div><dt>Server versions</dt><dd>Save explicitly from Export.</dd></div><div><dt>Personal backup</dt><dd>Download JSON to keep or transfer your model.</dd></div></dl><p>Sharing and collaboration are not available.</p></section>
    </aside></div>
    <section className="project-templates" id="project-template-catalog"><header className="projects-section-heading"><div><h2>Game and model templates</h2><p>Start from an authored example. Each creates an independent copy; RTP is not calibrated.</p></div><span>{templates.length} starting models</span></header><div className="template-list">{templates.map(template => <article className="template-row" key={template.name}><span className="template-kind">{template.kind}</span><h3>{template.name}</h3><p>{template.summary}</p><button className="btn" onClick={() => create(template.create())}>Create {template.name} project</button></article>)}</div></section>
    <section className="projects-server"><header className="projects-section-heading"><div><h2>Saved on the server</h2><p>Open a version as a separate browser draft. Saved inputs and run evidence stay pinned.</p></div><button className="btn" disabled={server.isFetching} onClick={() => void server.refetch()}>{server.isFetching ? 'Refreshing…' : 'Refresh saved projects'}</button></header>
      {error && errorArea === 'server' && <div className="projects-error" role="alert"><strong>Project could not be opened</strong><p>{error}</p><button className="btn" onClick={() => setError('')}>Dismiss error</button></div>}
      {server.isPending && <p className="projects-server-message" role="status">Loading saved projects…</p>}
      {server.isError && <div className="projects-error" role="alert"><p>{server.error.message}</p><button className="btn" onClick={() => void server.refetch()}>Retry saved projects</button></div>}
      {saved.length > 0 && <div className="project-list" aria-label="Server project versions">{saved.map(p => <article className="project-row" key={p.id}><div><h2>{p.name}</h2><p>Version {p.latestVersion} · <code>{p.id}</code></p></div><button className="btn" disabled={!!loadingId} onClick={async () => {
        invalidate(); const token = operation.current.token, controller = new AbortController(); pending.current = controller;
        const current = () => operation.current.mounted && token === operation.current.token && !controller.signal.aborted; setLoadingId(p.id); setErrorArea('server'); setError('');
        try { const response = await fetch(`${BASE_URL}/api/configs/${encodeURIComponent(p.id)}`, { signal: controller.signal }); if (!response.ok) throw new Error(`Unable to open saved project (HTTP ${response.status}).`); const detail = parseModelJson(await response.text()); if (current()) create(detail.config as Record<string, unknown>); }
        catch (reason) { if (current()) setError(reason instanceof Error ? reason.message : 'Unable to open saved project.'); }
        finally { if (current()) { pending.current = null; setLoadingId(null); } }
      }}>{loadingId === p.id ? 'Opening…' : 'Open saved version'}</button></article>)}</div>}
      {server.data && !saved.length && <div className="projects-server-message"><h3>{search ? 'No matching server versions' : 'No server versions saved yet'}</h3><p>{search ? 'Try another project name or ID.' : 'When a model is ready for a saved version, open Export and choose Save to server.'}</p></div>}
    </section><footer className="projects-footer"><span>Private models · independent projects</span><span>Browser drafts and saved server versions are separate.</span></footer>
  </section>;
}
