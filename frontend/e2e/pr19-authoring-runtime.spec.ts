import {type APIRequestContext, expect, test} from './fixtures';
import {pr19AuthoringProofs} from '../unit/fixtures/pr19AuthoringProofs';

const expected = new Map([
    ['aggregate-default', 3], ['aggregate-explicit', 3], ['aggregate-local-alias', 3], ['alias-undefined', 7], ['alias-null', 7], ['alias-state', 7], ['alias-board', 7],
    ['fold-initializer', 14], ['map-locals', 3], ['filter-locals', 2], ['aggregate-nested', 6],
    ['parameter-original', 7], ['parameter-bound', 7], ['parameter-composed-original', 7], ['parameter-composed-bound', 7],
    ['parameter-weighted-original', 7], ['parameter-weighted-bound', 7]
]);

async function post(request: APIRequestContext, path: string, data: unknown) {
    for (let attempt = 0; attempt < 3; attempt++) {
        const response = await request.post(path, {data});
        if (response.status() !== 429) return response;
        const pause = Number(response.headers()['retry-after'] ?? '60');
        console.log(`Explicit quota rejection for ${path}; wait ${pause}s before attempt ${attempt + 2}.`);
        await new Promise(resolve => setTimeout(resolve, Math.max(1, pause) * 1000));
    }
    throw new Error(`Compute quota did not recover for ${path}.`);
}

for (const {id, graph} of pr19AuthoringProofs()) test(`PR19 actual compiler/round: ${id}`, async ({request}) => {
    test.setTimeout(180000);
    const validation = await post(request, '/api/validate', {config: graph}), validated = await validation.json();
    expect(validated, JSON.stringify(validated)).toMatchObject({isValid: true});
    const response = await post(request, '/api/play/round', {config: graph, seed: 42, roundIndex: 0}),
        result = await response.json();
    expect(response.ok(), JSON.stringify(result)).toBe(true);
    const payout = expected.get(id);
    expect(result.win).toBe(payout);
    if (id.startsWith('aggregate-')) expect(result.win).not.toBe(20);
});
