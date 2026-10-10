import { definition, type MeasurementDefinition, type MetricDraft } from './model';
import type { components } from '../../api/generated-types';

export const checkLabels: Record<string, string> = { 'observation-integrity': 'Observation integrity', 'exact-zero-assertion': 'Exact zero / false residual', 'reference-support': 'No unexpected reference support', 'mean-equivalence': 'Reference statistic within precision' };
export const profileChecks = ['observation-integrity', 'exact-zero-assertion', 'reference-support', 'mean-equivalence'] as const;
export type ProfileCheck = typeof profileChecks[number];
export type VerificationCriterion = Omit<components['schemas']['VerificationCriterion'], 'check'> & { check: ProfileCheck };
export type VerificationProfile = Omit<components['schemas']['VerificationProfile'], 'criteria'> & { criteria: VerificationCriterion[] };
export type ProfileReport = components['schemas']['VerificationProfileReport'];
export type ProfileEvidence = components['schemas']['RunVerificationEvidence'];
export type RetainedProfile = Omit<components['schemas']['RetainedReferenceOfRunVerificationEvidence'], 'report'> & { report: ProfileEvidence };

export function allowedProfileChecks(metric: MeasurementDefinition): ProfileCheck[] {
  const o = metric.options;
  const boolean = o?.source === 'event' && o.subject === 'observation' || ['any', 'all'].includes(o?.reduction ?? '') && ['round', 'episode'].includes(o?.subject ?? '');
  const needsIndependentSubjects = boolean || ['probability', 'ratio'].includes(o?.referenceStatistic ?? '') || o?.pairRole === 'wager';
  return profileChecks.filter(check => check === 'observation-integrity' || check === 'exact-zero-assertion' && o?.assertion === 'zero' && o.subject === 'observation'
    || check === 'reference-support' && !!o?.referenceDistribution.length && !o.weight
    || check === 'mean-equivalence' && o?.referenceMean != null && !!o.tolerance && !o.weight && (o.independentSubjects || o.independentParents && !needsIndependentSubjects));
}
export function validProfile(value: unknown): value is VerificationProfile {
  if (!value || typeof value !== 'object') return false;
  const p = value as VerificationProfile;
  if (typeof p.name !== 'string' || !p.name.trim() || p.name.length > 80 || !Number.isFinite(p.familyConfidence) || p.familyConfidence < .5 || p.familyConfidence > .999999
    || !Array.isArray(p.criteria) || !p.criteria.length || p.criteria.length > 32) return false;
  const ids = new Set<string>();
  return p.criteria.every(c => {
    if (!c || typeof c.measurementId !== 'string' || !c.measurementId || !profileChecks.includes(c.check) || !Number.isSafeInteger(c.minimumCount) || c.minimumCount < 1 || c.minimumCount > 10_000_000) return false;
    const key = JSON.stringify([c.measurementId, c.check]); if (ids.has(key)) return false; ids.add(key); return true;
  });
}
export function sameProfile(a?: VerificationProfile | null, b?: VerificationProfile | null): boolean {
  return a == null || b == null ? a == null && b == null : a.name === b.name && a.familyConfidence === b.familyConfidence && JSON.stringify(a.criteria.map(c => [c.measurementId, c.check, c.minimumCount])) === JSON.stringify(b.criteria.map(c => [c.measurementId, c.check, c.minimumCount]));
}
export function validateProfile(profile: VerificationProfile, plan: MeasurementDefinition[]): void {
  if (!validProfile(profile)) throw new Error('Choose a profile name, 1–32 distinct checks, valid minimum counts and family confidence.');
  let alpha = 0;
  for (const c of profile.criteria) {
    const metric = plan.find(d => d.id === c.measurementId);
    if (!metric || !allowedProfileChecks(metric).includes(c.check)) throw new Error('A required measurement or its inference contract changed. Review the verification profile before launch.');
    if (c.check === 'mean-equivalence') alpha += (1 - metric.options!.confidence) / metric.options!.errorFamilySize;
  }
  if (!Number.isFinite(alpha) || alpha > (1 - profile.familyConfidence) * (1 + 1e-12)) throw new Error('The mean checks exceed the shared family error budget. Allocate interval budgets before saving.');
}
export function allocateProfile(metrics: MetricDraft[], profile: VerificationProfile): MetricDraft[] {
  const refs = profile.criteria.filter(c => c.check === 'mean-equivalence').map(c => c.measurementId);
  const proposed = metrics.map(metric => !refs.includes(metric.id) || !metric.options ? metric : { ...metric, options: { ...metric.options,
    confidence: Math.max(metric.options.confidence, profile.familyConfidence), errorFamilySize: Math.max(metric.options.errorFamilySize, refs.length) } });
  validateProfile(profile, proposed.map(definition)); return proposed;
}
