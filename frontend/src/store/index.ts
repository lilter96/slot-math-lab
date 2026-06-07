import { create } from 'zustand';

export type TabId = 'build' | 'simulate' | 'results' | 'export';
export type Mood = 'slate' | 'ocean' | 'violet' | 'steel';
export type Density = 'compact' | 'regular';

export interface Tweaks {
  accent: string;
  mood: Mood;
  grid: boolean;
  flow: boolean;
  density: Density;
}

export interface AppState {
  tab: TabId;
  setTab: (tab: TabId) => void;
  configName: string | null;
  setConfigName: (name: string | null) => void;
  tweaks: Tweaks;
  setTweak: <K extends keyof Tweaks>(key: K, value: Tweaks[K]) => void;
}

export const MOOD_HUE: Record<Mood, number> = {
  slate: 255,
  ocean: 230,
  violet: 290,
  steel: 210,
};

export const useAppStore = create<AppState>((set) => ({
  tab: 'build',
  setTab: (tab) => set({ tab }),
  configName: 'Untitled',
  setConfigName: (name) => set({ configName: name }),
  tweaks: {
    accent: '#46d39a',
    mood: 'slate',
    grid: true,
    flow: true,
    density: 'regular',
  },
  setTweak: (key, value) =>
    set((s) => ({ tweaks: { ...s.tweaks, [key]: value } })),
}));
