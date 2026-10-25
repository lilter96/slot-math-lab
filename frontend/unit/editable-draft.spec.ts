import { expect, test } from '@playwright/test';
import { parseEditableDraft } from '../src/lib/editableDraft';
const draft = { format: 'slotmath-editable-draft-v1', nodes: [{ id: 'a', type: 'draw', position: { x: 1, y: 2 }, data: { nodeType: 'draw', weightExpressionId: 'state.', unknown: 'keep' } }], edges: [], tables: { initialState: { count: 10000000000 } }, graphTrail: [], configName: 'Repair me', verificationSource: { forged: true }, authoringDraft: { projectId: 'foreign' }, resultsDraft: { forged: true } };
test('Editable backup retains invalid source but never restores imported evidence or project references', () => {
  const restored = parseEditableDraft(JSON.stringify(draft));
  expect(restored.nodes).toEqual(draft.nodes); expect(restored.tables).toEqual(draft.tables);
  expect(restored.verificationSource).toBeNull(); expect(restored.authoringDraft).toBeNull(); expect(restored.resultsDraft).toBeNull();
  const graphTrail = [{ mechanic: 'round', graph: { schemaVersion: '1.0.0', nodes: [], edges: [], mechanics: { round: { nodes: [], edges: [], expressions: { keep: { value: 'state.' } } } }, untouched: { keep: true } } }];
  expect(parseEditableDraft(JSON.stringify({ ...draft, graphTrail })).graphTrail).toEqual(graphTrail);
});
test('Malformed and unsafe backups are rejected before restoration', () => {
  for (const value of [{ ...draft, format: 'future-version' }, { ...draft, nodes: [draft.nodes[0], draft.nodes[0]] }, { ...draft, nodes: [{ ...draft.nodes[0], position: null }] }, { ...draft, edges: [{ id: 'bad', source: 'a', target: 'missing' }] }, { ...draft, graphTrail: [{ mechanic: 'missing', graph: {} }] }]) expect(() => parseEditableDraft(JSON.stringify(value))).toThrow();
  expect(() => parseEditableDraft(JSON.stringify(draft).replace('10000000000', '9007199254740993'))).toThrow('exact browser range');
});
