import { useSyncExternalStore } from 'react';
import { getDraftSaveState } from '../store';
const subscribe = (changed: () => void) => { window.addEventListener('slotmath:draft-save', changed); return () => window.removeEventListener('slotmath:draft-save', changed); };
/** A view-only subscription; displaying save state never schedules another graph save. */
export function useDraftSave() { return useSyncExternalStore(subscribe, getDraftSaveState, getDraftSaveState); }
