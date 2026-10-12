import { assertModelNumbers } from '../lib/modelJson';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { apiClient, BASE_URL } from './client';
import { useAppStore, type PluginEntry, type CustomMechanic, type GraphNode, type GraphEdge } from '../store';
import { HttpFailure, retryAfterMilliseconds } from '../lib/realtime/httpFailure';
import { buildConfigPayload, mapNodeToBackend } from '../lib/configPayload';

// ═══════════════════════════════════════════════════════════════════
// Feature flags API hook
// ═══════════════════════════════════════════════════════════════════

/** Backend-controlled UI feature gates (GET /api/features). */
export interface FeatureFlags {
  ai: boolean;
  autoTune: boolean;
  plugins: boolean;
  play: boolean;
}

/**
 * Fail-closed defaults: while the flags request is in flight, and whenever it
 * is missing or fails, every optional (non-1.0) surface stays hidden.
 */
export const DEFAULT_FEATURES: FeatureFlags = { ai: false, autoTune: false, plugins: false, play: false };

const FEATURES_KEY = ['features'];

/** Fetch UI feature flags. Raw fetch: /api/features is not in generated-types. */
export function useFeaturesQuery() {
  return useQuery({
    queryKey: FEATURES_KEY,
    queryFn: async () => {
      // Honor the configured API origin (VITE_API_URL); fall back to same-origin.
      const base = BASE_URL.replace(/\/+$/, '');
      const res = await fetch(`${base}/api/features`);
      if (!res.ok) throw new HttpFailure(`Failed to load feature flags: HTTP ${res.status}`, res.status, retryAfterMilliseconds(res.headers.get('Retry-After')));
      const data = (await res.json()) as Partial<FeatureFlags>;
      // Only explicit `true` enables a surface — anything else collapses to off.
      return {
        ai: data.ai === true,
        autoTune: data.autoTune === true,
        plugins: data.plugins === true,
        play: data.play === true,
      } satisfies FeatureFlags;
    },
    staleTime: Infinity,
    retry: (attempt, error) => error instanceof HttpFailure && error.status === 429 && error.retryAfterMs <= 60000 && attempt < 2,
    retryDelay: (_attempt, error) => error instanceof HttpFailure ? Math.max(1000, error.retryAfterMs) : 1000,
  });
}

// ═══════════════════════════════════════════════════════════════════
// Plugins API hooks
// ═══════════════════════════════════════════════════════════════════

const PLUGINS_KEY = ['plugins'];

/** Fetch plugins from backend, populate store on success. */
export function usePluginsQuery() {
  return useQuery({
    queryKey: PLUGINS_KEY,
    queryFn: async () => {
      const { data, error, response } = await apiClient.GET('/api/plugins');
      if (!response.ok || error) throw new Error(`Plugin registry unavailable (HTTP ${response.status}).`);
      const payload: unknown = data;
      if (!Array.isArray(payload)) throw new Error('No plugin registry returned. Retry when the API is available.');
      return payload.map((value: unknown) => {
        if (!value || typeof value !== 'object') throw new Error('Invalid plugin registry entry. Retry when the API is available.');
        const entry = value as Record<string, unknown>, version = entry.version;
        if (typeof entry.pluginId !== 'string' || typeof entry.contract !== 'string' || typeof entry.isConformant !== 'boolean'
          || (version != null && typeof version !== 'string')) throw new Error('Invalid plugin registry entry. Retry when the API is available.');
        return { pluginId: entry.pluginId, contract: entry.contract, isConformant: entry.isConformant, version };
      });
    },
    staleTime: 10_000,
  });
}

/** Sync store plugins from backend on mount. Falls back gracefully when backend is unavailable. */
export function useSyncPluginsFromBackend() {
  const setPluginsFromBackend = useAppStore((s) => s.setPluginsFromBackend);
  const setBackendAvailable = useAppStore((s) => s.setBackendAvailable);

  useEffect(() => {
    let cancelled = false;
    apiClient.GET('/api/plugins')
      .then(({ data, error }) => {
        if (cancelled) return;
        if (!error && data) {
          setBackendAvailable(true);
          const plugins: PluginEntry[] = (data as Array<Record<string, unknown>>).map((p: Record<string, unknown>) => ({
            pluginId: String(p.pluginId ?? ''),
            contract: (p.contract as PluginEntry['contract']) ?? 'IEvaluator',
            version: String(p.version ?? '1.0.0'),
            isConformant: Boolean(p.isConformant),
            conformanceNote: p.conformanceNote ? String(p.conformanceNote) : undefined,
            forcesSampledRegime: Boolean(p.forcesSampledRegime ?? true),
          }));
          setPluginsFromBackend(plugins);
        } else {
          setBackendAvailable(false);
        }
      })
      .catch(() => {
        if (!cancelled) setBackendAvailable(false);
      });
    return () => { cancelled = true; };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
}

/** Register a plugin via the backend API. */
export function useRegisterPlugin() {
  const queryClient = useQueryClient();
  const registerInStore = useAppStore((s) => s.registerPlugin);

  return useMutation({
    mutationFn: async (plugin: { pluginId: string; contract: 'IEvaluator' | 'ITransform' | 'WeightSource' }) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const { data, error } = await apiClient.POST('/api/plugins', { body: plugin as any });
      if (error) throw new Error(`Failed to register plugin: ${error}`);
      return data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: PLUGINS_KEY });
      registerInStore({
        pluginId: variables.pluginId,
        contract: variables.contract,
        version: '1.0.0',
        isConformant: true,
        forcesSampledRegime: true,
      });
    },
  });
}

// ═══════════════════════════════════════════════════════════════════
// Configs API hooks (mechanics are part of config)
// ═══════════════════════════════════════════════════════════════════

const CONFIGS_KEY = ['configs'];

/** Fetch all configs from backend. Populates mechanics from config data. */
export function useConfigsQuery() {
  return useQuery({
    queryKey: CONFIGS_KEY,
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/configs');
      if (error) throw new Error(`Failed to load configs: ${error}`);
      return data ?? [];
    },
    staleTime: 10_000,
  });
}

/** Map frontend graph edges to the backend Edge shape. */
function mapEdgesToBackend(edges: GraphEdge[]): Record<string, unknown>[] {
  return edges.map((e) => ({
    id: e.id,
    sourceNodeId: e.source,
    sourcePort: e.sourceHandle ?? 'out',
    targetNodeId: e.target,
    targetPort: e.targetHandle ?? 'in',
  }));
}

/** Save a config (including mechanics) to the backend. */
export function useSaveConfig() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (config: { id?: string; name: string; payload?: Record<string, unknown>; mechanics?: CustomMechanic[]; nodes?: GraphNode[]; edges?: GraphEdge[] }) => {
      // The backend deserializes nodes/edges into strongly-typed polymorphic
      // models — raw React Flow shapes are rejected, so everything must go
      // through the canonical mapping.
      const body = {
        config: config.payload ?? {
          ...buildConfigPayload(config.nodes ?? [], config.edges ?? [], { name: config.name, tables: useAppStore.getState().tables }),
          mechanics: config.mechanics ? Object.fromEntries(
            config.mechanics.map((m) => [m.id, {
              name: m.name,
              description: m.description,
              nodes: m.nodes.map(mapNodeToBackend),
              edges: mapEdgesToBackend(m.edges),
            }])
          ) : undefined,

        },
      };

      assertModelNumbers(body.config);
      if (config.id) {
        const { data, error } = await apiClient.PUT('/api/configs/{id}', {
          params: { path: { id: config.id } },
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          body: body as any,
        });
        if (error) throw new Error(`Failed to update config: ${error}`);
        return data;
      } else {
        const { data, error } = await apiClient.POST('/api/configs', {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          body: body as any,
        });
        if (error) throw new Error(`Failed to create config: ${error}`);
        return data;
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CONFIGS_KEY });
    },
  });
}

/** Load mechanics from a saved config on mount. */
export function useLoadMechanicsFromBackend(configId?: string) {
  return useQuery({
    queryKey: [...CONFIGS_KEY, configId],
    queryFn: async () => {
      if (!configId) return null;
      const { data, error } = await apiClient.GET('/api/configs/{id}', {
        params: { path: { id: configId } },
      });
      if (error) throw new Error(`Failed to load config: ${error}`);
      return data;
    },
    enabled: !!configId,
    staleTime: 30_000,
  });
}
