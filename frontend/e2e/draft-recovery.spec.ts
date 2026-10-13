import {expect, type Page, test} from './fixtures';
import {readFile} from 'node:fs/promises';

const key = 'slotmath-draft-v1';
const source = '{"projectId":"original","configName":"Do not round me","nodes":[],"edges":[],"tables":{"initialState":{"count":9007199254740993}},"projects":{"another":{"tables":{"private":"retained"}}}}';

async function recovery(page: Page) {
    const pending = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Download stored workspace', exact: true}).click();
    return readFile((await (await pending).path())!, 'utf8');
}

async function seed(page: Page, value: string, blocked = false) {
    await page.addInitScript(({key, value, blocked}) => {
        if (!sessionStorage.getItem('recovery-seeded')) {
            localStorage.setItem(key, value);
            sessionStorage.setItem('recovery-seeded', 'yes');
        }
        if (blocked) {
            const original = Storage.prototype.setItem;
            Storage.prototype.setItem = function (k, v) {
                if (k.startsWith(key)) throw new DOMException('Quota exceeded', 'QuotaExceededError');
                return original.call(this, k, v);
            };
        }
    }, {key, value, blocked});
}

test('Unsafe stored workspace is retained byte-for-byte while independent new drafts survive reload', async ({page}) => {
    await seed(page, source);
    await page.goto('/projects');
    await expect(page.getByRole('alert')).toContainText('recovery copy');
    expect(await recovery(page)).toBe(source);
    const backup = await page.evaluate(key => localStorage.getItem(`${key}:recovery-current`), key);
    expect(backup).toMatch(/:recovery:/);
    await page.getByRole('button', {name: 'New blank project', exact: true}).click();
    await expect.poll(() => page.evaluate(key => JSON.parse(localStorage.getItem(key) ?? '{}').configName, key)).toBe('Untitled model');
    await page.reload();
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toHaveAttribute('title', 'Untitled model');
    expect(await recovery(page)).toBe(source);
    expect(await page.evaluate(key => localStorage.getItem(`${key}:recovery-current`), key)).toBe(backup);
});

test('Failed recovery backup freezes browser writes and retains original JSON after new edits and reload', async ({page}) => {
    await seed(page, source, true);
    await page.goto('/projects');
    await expect(page.getByRole('alert')).toContainText('Autosave is paused');
    expect(await recovery(page)).toBe(source);
    await page.getByRole('button', {name: 'New blank project', exact: true}).click();
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toHaveAttribute('title', 'Untitled model');
    await page.waitForTimeout(500); // Cross the actual 400 ms persistence debounce.
    expect(await page.evaluate(key => localStorage.getItem(key), key)).toBe(source);
    await page.reload();
    await expect(page.getByRole('alert')).toContainText('Autosave is paused');
    expect(await recovery(page)).toBe(source);
});

test('Malformed stored JSON remains downloadable and is not repeatedly copied on reload', async ({page}) => {
    const malformed = '{"nodes":[],"edges":[],"unfinished":';
    await seed(page, malformed);
    await page.goto('/projects');
    expect(await recovery(page)).toBe(malformed);
    const count = () => page.evaluate(key => Object.keys(localStorage).filter(k => k.startsWith(`${key}:recovery:`)).length, key);
    expect(await count()).toBe(1);
    await page.reload();
    expect(await recovery(page)).toBe(malformed);
    expect(await count()).toBe(1);
});

test('Safe wide draft restores unknown fields and remains exact through browser autosave', async ({page}) => {
    const safe = '{"projectId":"safe","configName":"Wide safe draft","nodes":[{"id":"sink","type":"sink","position":{"x":0,"y":0},"data":{"nodeType":"sink","label":"Sink"}}],"edges":[],"tables":{"initialState":{"count":9007199254740991},"metadata":"9223372036854775807"}}';
    await seed(page, safe);
    await page.goto('/build');
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toHaveAttribute('title', 'Wide safe draft');
    await expect(page.getByRole('button', {name: 'Download stored workspace'})).toHaveCount(0);
    await page.goto('/projects');
    await page.getByRole('button', {name: 'Rename', exact: true}).click();
    await page.getByLabel('Project name', {exact: true}).fill('Wide safe edited');
    await page.getByRole('button', {name: 'Save name', exact: true}).click();
    await expect.poll(() => page.evaluate(key => JSON.parse(localStorage.getItem(key)!).configName, key)).toBe('Wide safe edited');
    await page.reload();
    await expect(page.getByRole('link', {
        name: 'Choose project',
        exact: true
    })).toHaveAttribute('title', 'Wide safe edited');
    expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!).tables.metadata, key)).toBe('9223372036854775807');
    expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!).tables.initialState.count, key)).toBe(Number.MAX_SAFE_INTEGER);
});


test('Recovery lookup cannot read another verified owner namespace', async ({page}) => {
    await page.route('**/api/auth/status', route => route.fulfill({
        json: {
            required: true,
            authenticated: true,
            userId: 'recovery-bob'
        }
    }));
    await page.addInitScript(source => {
        const foreign = 'slotmath-draft-v1:user:recovery-alice:recovery:original';
        localStorage.setItem(foreign, source);
        localStorage.setItem('slotmath-draft-v1:user:recovery-bob:recovery-current', foreign);
    }, source);
    await page.goto('/projects');
    await expect(page.getByRole('heading', {name: 'My projects', exact: true})).toBeVisible();
    await expect(page.getByRole('button', {name: 'Download stored workspace', exact: true})).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem('slotmath-draft-v1:user:recovery-alice:recovery:original'))).toBe(source);
});

test('Unsupported raw numeric edits keep the last safe draft and valid edits resume autosave', async ({page}) => {
    const safe = '{"projectId":"numeric-write","configName":"Numeric write","nodes":[{"id":"sink","type":"sink","position":{"x":0,"y":0},"data":{"nodeType":"sink","label":"Sink"}}],"edges":[],"tables":{"initialState":{"count":1}}}';
    await seed(page, safe);
    await page.goto('/build');
    await page.getByRole('button', {name: 'Edit project data and expressions', exact: true}).click();
    const field = page.getByLabel('State field value', {exact: true});
    await field.fill('9007199254740993');
    await expect(page.getByRole('alert')).toContainText('exact browser range');
    expect(await page.evaluate(key => JSON.parse(localStorage.getItem(key)!).tables.initialState.count, key)).toBe(1);
    await field.fill('2');
    await expect.poll(() => page.evaluate(key => JSON.parse(localStorage.getItem(key)!).tables.initialState.count, key)).toBe(2);
    await expect(page.getByRole('alert')).toHaveCount(0);
});
