import {expect, test} from '@playwright/test';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {pr19AuthoringProofs} from './fixtures/pr19AuthoringProofs';
import {indexGraphSources} from '../src/lib/authoring/sourceIndex';

test('PR19: emit actual lowerer input programs for independently expected GraphCompiler/runtime checks', async () => {
    const folder = '../backend/SlotMath.Core.Tests/TestData/AuthoringAudit';
    await mkdir(folder, {recursive: true});
    const proofs = pr19AuthoringProofs();
    expect(proofs).toHaveLength(17);
    for (const {id, graph} of proofs) {
        expect(indexGraphSources(graph).unresolvedEffects.filter(effect => effect.active), id).toEqual([]);
        const text = JSON.stringify(graph, null, 2) + '\n', file = `${folder}/${id}.json`;
        if (process.env.PR19_WRITE_INPUTS === '1') await writeFile(file, text);
        else expect(await readFile(file, 'utf8'), `The checked-in input program must match the actual lowerer: ${id}`).toBe(text);
    }
});
