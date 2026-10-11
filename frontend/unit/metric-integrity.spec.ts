import { test, expect } from '@playwright/test';
import { metricProvenance } from '../src/lib/metricEvidence';
test('Unknown, failed and budget-limited calculation methods have no provenance badge', () => {
  expect(metricProvenance({ provenance: 'NeedsFullRun' })).toBeNull();
  expect(metricProvenance({ provenance: 'Error' })).toBeNull();
});
test('Sampled evidence requires completed samples and never invents uncertainty', () => {
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled', sampleCount: 0 })).toBeNull();
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled' })).toBeNull();
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled', sampleCount: 1, stdErr: 0 })).toEqual({ kind: 'Sampled', n: 1, stdErr: undefined });
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled', sampleCount: 100 })).toEqual({ kind: 'Sampled', n: 100, stdErr: undefined });
});
test('A computed zero retains its actual exact method', () => {
  expect(metricProvenance({ rtp: 0, provenance: 'Exact' })).toEqual({ kind: 'Exact' });
});
test('Interval proof requires ordered finite returned bounds and retains the actual range', () => {
  expect(metricProvenance({ rtp: 0.9, provenance: 'ExactInterval' })).toBeNull();
  expect(metricProvenance({ rtp: 0.9, provenance: 'ExactInterval', lo: 1, hi: 0 })).toBeNull();
  expect(metricProvenance({ rtp: 0.9, provenance: 'ExactInterval', lo: 0.8, hi: 1 })).toEqual({ kind: 'ExactInterval', note: 'RTP ratio interval: 0.8 to 1' });
});
