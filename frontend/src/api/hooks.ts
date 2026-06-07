import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { apiClient } from './client';
import { useAppStore, type PluginEntry, type CustomMechanic } from '../store';

// ═══════════════════════════════════════════════════════════════════
// Plugins API hooks
// ═══════════════════════════════════════════════════════════════════

const PLUGINS_KEY = ['plugins'];

/** Fetch plugins from backend, populate store on success. */
export function usePluginsQuery() {
  return useQuery({
    queryKey: PLUGINS_KEY,
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/plugins');
      if (error) throw new Error(`Failed to load plugins: ${error}`);
      return data ?? [];
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
            forcesSampledRegime: true,
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

/** Remove a plugin via the backend. Note: backend may not support DELETE on plugins, so this is optimistic. */
export function useRemovePlugin() {
  const queryClient = useQueryClient();
  const removeFromStore = useAppStore((s) => s.removePlugin);

  return useMutation({
    mutationFn: async (pluginId: string) => {
      // Backend doesn't have DELETE /api/plugins/{id}, so we just invalidate
      return pluginId;
    },
    onSuccess: (pluginId) => {
      queryClient.invalidateQueries({ queryKey: PLUGINS_KEY });
      removeFromStore(pluginId);
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

/** Save a config (including mechanics) to the backend. */
export function useSaveConfig() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (config: { id?: string; name: string; mechanics?: CustomMechanic[]; nodes?: unknown[]; edges?: unknown[] }) => {
      const body = {
        config: {
          schemaVersion: '1.0.0',
          name: config.name,
          mechanics: config.mechanics ? Object.fromEntries(
            config.mechanics.map((m) => [m.id, { name: m.name, description: m.description, nodes: m.nodes, edges: m.edges }])
          ) : undefined,
          nodes: config.nodes,
          edges: config.edges,
        },
      };

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
