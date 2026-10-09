import { create } from 'zustand';
import { definition, type MetricDraft } from './model';
const key = 'slotmath-measurements-v1';
export type Widget = 'rtp' | 'precision' | 'hit' | 'max' | 'speed' | 'volatility' | 'convergence' | 'distribution' | 'reference' | 'throughput' | 'uncertainty';
export const widgets: { id: Widget; name: string }[] = [
  { id: 'rtp', name: 'Observed RTP' }, { id: 'precision', name: 'CI half-width' }, { id: 'hit', name: 'Hit frequency' },
  { id: 'max', name: 'Maximum observed' }, { id: 'speed', name: 'Throughput' }, { id: 'volatility', name: 'Payout volatility' },
  { id: 'convergence', name: 'RTP convergence chart' }, { id: 'distribution', name: 'Payout histogram' }, { id: 'reference', name: 'Reference check' },
  { id: 'throughput', name: 'Throughput chart' }, { id: 'uncertainty', name: 'Precision chart' }];
interface Workspace { metrics: MetricDraft[]; hiddenWidgets: Widget[] }
function restore(): Workspace {
  try {
    const data = JSON.parse(localStorage.getItem(key) ?? 'null');
    if (!Array.isArray(data?.metrics) || data.metrics.length > 32) throw new Error();
    data.metrics.forEach((metric: MetricDraft) => definition(metric));
    return { metrics: data.metrics, hiddenWidgets: (data.hiddenWidgets ?? []).filter((id: Widget) => widgets.some(w => w.id === id)) };
  } catch { return { metrics: [], hiddenWidgets: [] }; }
}
export const useMeasurementWorkspace = create<Workspace>(() => restore());
useMeasurementWorkspace.subscribe(s => { try { localStorage.setItem(key, JSON.stringify(s)); } catch { /* Draft remains usable for this tab. */ } });
export function saveMetric(metric: MetricDraft) { useMeasurementWorkspace.setState(s => ({ metrics: s.metrics.some(m => m.id === metric.id) ? s.metrics.map(m => m.id === metric.id ? metric : m) : [...s.metrics, metric] })); }
export function removeMetric(id: string) { useMeasurementWorkspace.setState(s => ({ metrics: s.metrics.filter(m => m.id !== id) })); }
