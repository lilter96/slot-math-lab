import {useAppStore} from '../../store';
import {assertModelNumbers, parseModelJson} from '../modelJson';
import {peekRootProject, projectEditorState, switchProject} from '../projectFiles';
import {type AuthoringViews, parseAuthoringViews} from './viewMetadata';

export interface ProjectEnvelope {
    format: 'slotmath-project-v1';
    graph: Record<string, unknown>;
    views: AuthoringViews | null
}

export function projectEnvelope(): ProjectEnvelope {
    const graph = peekRootProject();
    if (!graph) throw new Error('Open a project before exporting.');
    return {
        format: 'slotmath-project-v1',
        graph: structuredClone(graph),
        views: parseAuthoringViews(useAppStore.getState().authoringViews)
    };
}

export function parseProjectImport(text: string): { graph: Record<string, unknown>; views: AuthoringViews | null } {
    const value = parseModelJson(text);
    if (value.format !== 'slotmath-project-v1' && (value.format === undefined || (Array.isArray(value.nodes) && Array.isArray(value.edges)))) {
        projectEditorState(value);
        return {graph: value, views: null};
    }
    if (value.format !== 'slotmath-project-v1' || !value.graph || typeof value.graph !== 'object' || Array.isArray(value.graph) || Object.keys(value).some(key => !['format', 'graph', 'views'].includes(key))) throw new Error('Choose a GraphConfig JSON or a supported slotmath-project-v1 file.');
    const graph = value.graph as Record<string, unknown>;
    assertModelNumbers(graph);
    projectEditorState(graph);
    return {graph, views: parseAuthoringViews(value.views)};
}

export function importProjectFile(text: string): void {
    const imported = parseProjectImport(text);
    switchProject(imported.graph, imported.views);
}
