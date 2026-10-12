export interface AuthoringViews {
    version: 1;
    studio?: {
        tab: 'sources' | 'paytables' | 'paylines' | 'symbols';
        selectedSource: string | null;
        pinnedSources: string[]
    };
    machine?: {
        positions: Record<string, { x: number; y: number }>;
        viewport: { x: number; y: number; zoom: number };
        viewports?: Record<string, { x: number; y: number; zoom: number }>;
        expandedSources: string[]
    };
    symbolAssets?: Record<string, string>;
}

const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const keys = (value: Record<string, unknown>, allowed: string[]) => Object.keys(value).every(key => allowed.includes(key));
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === 'string');
const point = (value: unknown) => object(value) && keys(value, ['x', 'y']) && typeof value.x === 'number' && Number.isFinite(value.x) && typeof value.y === 'number' && Number.isFinite(value.y);

/** Only non-executable view data is allowed. Payouts, weights, actions and transitions belong to GraphConfig. */
export function parseAuthoringViews(value: unknown): AuthoringViews | null {
    if (value === undefined || value === null) return null;
    const fail = () => {
        throw new Error('Unsupported authoring view metadata. Keep the original project file for recovery.');
    };
    if (!object(value) || value.version !== 1 || !keys(value, ['version', 'studio', 'machine', 'symbolAssets'])) return fail();
    if (value.studio !== undefined) {
        const studio = value.studio;
        if (!object(studio) || !keys(studio, ['tab', 'selectedSource', 'pinnedSources']) || !['sources', 'paytables', 'paylines', 'symbols'].includes(String(studio.tab))
            || (studio.selectedSource !== null && typeof studio.selectedSource !== 'string') || !strings(studio.pinnedSources)) return fail();
    }
    if (value.machine !== undefined) {
        const machine = value.machine;
        if (!object(machine) || !keys(machine, ['positions', 'viewport', 'viewports', 'expandedSources']) || !object(machine.positions) || !Object.values(machine.positions).every(point) || !strings(machine.expandedSources)) return fail();
        const viewport = machine.viewport;
        if (!object(viewport) || !keys(viewport, ['x', 'y', 'zoom']) || !point({
            x: viewport.x,
            y: viewport.y
        }) || typeof viewport.zoom !== 'number' || !Number.isFinite(viewport.zoom) || viewport.zoom <= 0) return fail();
        if (machine.viewports !== undefined && (!object(machine.viewports) || !Object.values(machine.viewports).every(value => object(value) && keys(value, ['x', 'y', 'zoom']) && point({
            x: value.x,
            y: value.y
        }) && typeof value.zoom === 'number' && Number.isFinite(value.zoom) && value.zoom > 0))) return fail();
    }
    if (value.symbolAssets !== undefined && (!object(value.symbolAssets) || !Object.values(value.symbolAssets).every(asset => typeof asset === 'string'))) return fail();
    return structuredClone(value) as unknown as AuthoringViews;
}
