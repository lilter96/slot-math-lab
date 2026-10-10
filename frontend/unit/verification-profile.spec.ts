import { test, expect } from '@playwright/test';
import { newMetric, definition } from '../src/lib/measurements/model';
import { defaultOptions } from '../src/lib/measurements/analysis';
import { allocateProfile, allowedProfileChecks, sameProfile, validateProfile, validProfile, type VerificationProfile } from '../src/lib/measurements/profile';
const profile: VerificationProfile = { name: 'Critical means', familyConfidence: .95, criteria: [{ measurementId: 'x', check: 'mean-equivalence', minimumCount: 100 }, { measurementId: 'y', check: 'mean-equivalence', minimumCount: 200 }] };
const metrics = ['x', 'y'].map(id => ({ ...newMetric(id), name: id, options: { ...defaultOptions(), independentSubjects: true, referenceMean: 1, tolerance: .2 } }));
test('Predeclared family allocation updates only selected interval settings and preserves stronger prior budgets', () => {
  expect(() => validateProfile(profile, metrics.map(definition))).toThrow(/family error/);
  const allocated = allocateProfile(metrics, profile);
  expect(allocated.map(m => m.options?.errorFamilySize)).toEqual([2, 2]); expect(metrics.map(m => m.options?.errorFamilySize)).toEqual([1, 1]);
  validateProfile(profile, allocated.map(definition));
  const stronger = { ...metrics[0], options: { ...metrics[0].options, confidence: .99, errorFamilySize: 8 } };
  expect(allocateProfile([stronger, metrics[1]], profile)[0]).toEqual(stronger);
  expect(allocated.map(m => definition(m).value)).toEqual(metrics.map(m => definition(m).value));
});
test('Nonrejection, weighted or dependent references and stale measurement identities cannot pass profile authoring', () => {
  expect(allowedProfileChecks(definition({ ...metrics[0], options: { ...metrics[0].options, independentSubjects: false } }))).not.toContain('mean-equivalence');
  expect(allowedProfileChecks(definition({ ...metrics[0], options: { ...metrics[0].options, independentSubjects: false, independentParents: true, referenceStatistic: 'probability' } }))).not.toContain('mean-equivalence');
  expect(allowedProfileChecks({ ...definition(metrics[0]), options: { ...metrics[0].options, subject: 'round', assertion: 'zero' } })).not.toContain('exact-zero-assertion');
  expect(validProfile({ ...profile, criteria: [{ measurementId: 'x', check: 'distribution-goodness-of-fit', minimumCount: 1 }] })).toBe(false);
  expect(validProfile({ ...profile, criteria: [profile.criteria[0], profile.criteria[0]] })).toBe(false);
  expect(() => allocateProfile([metrics[0]], profile)).toThrow(/changed/);
  expect(sameProfile(profile, { ...profile, criteria: [{ ...profile.criteria[0], minimumCount: 99 }, profile.criteria[1]] })).toBe(false);
  expect(sameProfile(null, undefined)).toBe(true);
});
