import { create } from 'zustand';

export type TabId = 'build' | 'simulate' | 'results' | 'export';

export interface AppState {
  activeTab: TabId;
  setActiveTab: (tab: TabId) => void;
  configName: string | null;
  setConfigName: (name: string | null) => void;
}

export const useAppStore = create<AppState>((set) => ({
  activeTab: 'build',
  setActiveTab: (tab) => set({ activeTab: tab }),
  configName: null,
  setConfigName: (name) => set({ configName: name }),
}));
