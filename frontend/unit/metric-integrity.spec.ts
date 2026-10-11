import { test, expect } from '@playwright/test';
import { metricProvenance } from '../src/lib/metricEvidence';
test('Unknown, failed and budget-limited calculation methods have no provenance badge', () => {
  expect(metricProvenance({ provenance: 'NeedsFullRun' })).toBeNull();
  expect(metricProvenance({ provenance: 'Error' })).toBeNull();
});
test('Sampled evidence requires completed samples and never invents uncertainty', () => {
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled', sampleCount: 0 })).toBeNull();
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled' })).toBeNull();
  expect(metricProvenance({ rtp: 0, provenance: 'Sampled', sampleCount: 100 })).toEqual({ kind: 'Sampled', n: 100, stdErr: undefined });
});
test('A computed zero retains its actual exact method', () => {
  expect(metricProvenance({ rtp: 0, provenance: 'Exact' })).toEqual({ kind: 'Exact' });
});
