import {type APIRequestContext, expect, test} from './fixtures';

test.setTimeout(180000);

async function signInWithQuota(request: APIRequestContext, userId: string) {
    for (let attempt = 0; attempt < 3; attempt++) {
        const response = await request.post('/api/auth/token', {data: {userId}});
        if (response.status() !== 429) {
            expect(response.ok()).toBe(true);
            return;
        }
        const seconds = Number(response.headers()['retry-after'] ?? '1');
        await new Promise(resolve => setTimeout(resolve, Math.max(1, Number.isFinite(seconds) ? seconds : 1) * 1000));
    }
    throw new Error('Sign-in quota did not recover');
}

test('Personal projects retain nested edits, independent copies and different game geometry', {tag: '@critical'}, async ({page}) => {
    await page.goto('/');
    await expect(page.getByRole('heading', {name: 'My projects', exact: true})).toBeVisible();
    await expect(page.locator('.project-list[aria-label="Personal projects"] .project-row')).toHaveCount(0);
    await page.getByRole('button', {name: 'Create Nine Lives · reference project', exact: true}).click();
    await expect(page.locator('.structure-board > span')).toHaveCount(30);
    await page.getByRole('tab', {name: 'Features', exact: true}).click();
    await page.getByLabel('Awarded free spins').fill('2');
    await page.getByRole('button', {name: 'Validate & apply free spins'}).click();
    await expect(page.getByRole('button', {name: 'Undo free-spin change'})).toBeVisible({timeout: 70000});
    await page.getByRole('tab', {name: 'Overview', exact: true}).click();
    await page.getByRole('button', {name: /Free spins.*Open logic/}).click();
    await expect(page.getByTestId('model-navigation')).toContainText('free-spin');
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    await page.getByRole('button', {name: 'Create Ancient Dragon project', exact: true}).click();
    await expect(page.locator('.overview-page').getByRole('heading', {
        name: 'Ancient Dragon',
        exact: true
    })).toBeVisible();
    await expect(page.locator('.structure-board > span')).toHaveCount(15);
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    await page.getByRole('button', {name: 'Create Nine Lives · reference project', exact: true}).click();
    await page.getByRole('tab', {name: 'Features', exact: true}).click();
    await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
    await page.getByLabel('Awarded free spins').fill('4'); // staged form belongs only to this copy
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    const rows = page.locator('.project-list[aria-label="Personal projects"] .project-row');
    await expect(rows).toHaveCount(3);
    await rows.first().getByRole('button', {name: 'Open project', exact: true}).click();
    await page.getByRole('tab', {name: 'Build', exact: true}).click();
    await expect(page.getByTestId('model-navigation')).toContainText('free-spin');
    await page.getByRole('tab', {name: 'Features', exact: true}).click();
    await expect(page.getByLabel('Awarded free spins')).toHaveValue('2');
    await expect.poll(() => page.evaluate(() => Object.keys(JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? '{}').projects ?? {}).length)).toBe(3);
    await page.reload();
    await expect(page.getByLabel('Awarded free spins')).toHaveValue('2');
    await page.getByRole('tab', {name: 'Export', exact: true}).click();
    await expect(page.getByRole('button', {name: /share/i})).toHaveCount(0);
});

test('Verified account changes isolate browser projects and restore only the returning owner', {tag: '@critical'}, async ({
                                                                                                                              page,
                                                                                                                              context
                                                                                                                          }) => {
    const alice = 'browser-alice-' + Date.now(), bob = 'browser-bob-' + Date.now();
    const signIn = async (userId: string) => {
        await signInWithQuota(context.request, userId);
        await page.goto('/projects');
    };
    await signIn(alice);
    await page.getByRole('button', {name: 'Create Ancient Dragon project', exact: true}).click();
    await expect(page.locator('.structure-board > span')).toHaveCount(15);
    await expect.poll(() => page.evaluate(user => !!localStorage.getItem(`slotmath-draft-v1:user:${user}`), alice)).toBe(true);
    await signIn(bob);
    await expect(page.locator('.project-list[aria-label="Personal projects"] .project-row')).toHaveCount(0);
    await page.getByRole('button', {name: 'Create X7 Club project', exact: true}).click();
    await expect(page.getByRole('heading', {name: 'X7 Club', exact: true, level: 1})).toBeVisible();
    await signIn(alice);
    const rows = page.locator('.project-list[aria-label="Personal projects"] .project-row');
    await expect(rows).toHaveCount(1);
    await expect(rows).toContainText('Ancient Dragon');
    await expect(rows).not.toContainText('X7 Club');
    await rows.getByRole('button', {name: 'Open project', exact: true}).click();
    await expect(page.locator('.structure-board > span')).toHaveCount(15);
});

test('Unavailable or unsigned required session does not hydrate private browser drafts', {tag: '@critical'}, async ({page}) => {
    await page.addInitScript(() => localStorage.setItem('slotmath-draft-v1:user:someone', JSON.stringify({
        nodes: [],
        edges: [],
        configName: 'Private name'
    })));
    await page.route('**/api/auth/status', route => route.fulfill({status: 503, json: {}}));
    await page.goto('/projects');
    await expect(page.getByRole('heading', {name: 'Workspace unavailable'})).toBeVisible();
    await expect(page.getByText('Private name')).toHaveCount(0);
    await page.unroute('**/api/auth/status');
    await page.route('**/api/auth/status', route => route.fulfill({
        json: {
            required: true,
            authenticated: false,
            userId: null
        }
    }));
    await page.reload();
    await expect(page.getByRole('button', {name: 'Sign in', exact: true})).toBeVisible();
    await expect(page.getByRole('tab', {name: 'My projects', exact: true})).toHaveCount(0);
});

test('Validation finishing after a project switch cannot apply edits to an identical copy', {tag: '@critical'}, async ({page}) => {
    let finish: () => void = () => {
    };
    const gate = new Promise<void>(resolve => {
        finish = resolve;
    });
    let captured = false;
    await page.route('**/api/validate', async route => {
        captured = true;
        await gate;
        await route.fulfill({json: {isValid: true, errors: []}});
    });
    await page.goto('/model/features?project=nine-lives');
    await page.getByLabel('Awarded free spins').fill('1');
    await page.getByRole('button', {name: 'Validate & apply free spins'}).click();
    await expect.poll(() => captured).toBe(true);
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    await page.getByRole('button', {name: 'Create Nine Lives · reference project', exact: true}).click();
    await page.getByRole('tab', {name: 'Features', exact: true}).click();
    finish();
    await expect(page.getByLabel('Awarded free spins')).toHaveValue('9');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('slotmath-draft-v1') ?? '{}').tables?.initialState?.freeSpins)).toBe(9);
});

test('A rejected browser save keeps the current model available and gives an explicit backup path', {tag: '@critical'}, async ({page}) => {
    await page.addInitScript(() => {
        const original = Storage.prototype.setItem;
        Storage.prototype.setItem = function (key, value) {
            if (key.startsWith('slotmath-draft-v1')) throw new DOMException('Quota exceeded', 'QuotaExceededError');
            original.call(this, key, value);
        };
    });
    await page.goto('/projects');
    await page.getByRole('button', {name: 'Create Ancient Dragon project', exact: true}).click();
    await expect(page.getByRole('alert').filter({hasText: 'Browser storage could not save'})).toBeVisible();
    await page.getByRole('link', {name: 'Back up this project', exact: true}).click();
    const downloaded = page.waitForEvent('download');
    await page.getByRole('button', {name: 'Download JSON', exact: true}).click();
    const {readFile} = await import('node:fs/promises');
    const config = JSON.parse(await readFile((await (await downloaded).path())!, 'utf8'));
    expect(config.name).toBe('Ancient Dragon');
    expect(config.boardConfig).toMatchObject({columns: 5, rows: 3});
    expect(Object.keys(config.mechanics).length).toBeGreaterThan(0);
});

test('Session verification respects Retry-After before opening any saved projects', {tag: '@critical'}, async ({page}) => {
    const attempts: number[] = [];
    await page.route('**/api/auth/status', route => {
        attempts.push(Date.now());
        return attempts.length === 1 ? route.fulfill({status: 429, headers: {'Retry-After': '1'}, body: ''})
            : route.fulfill({json: {authenticated: false, required: false, userId: null}});
    });
    await page.goto('/projects');
    await expect(page.getByRole('status')).toContainText('server requested a 1s pause');
    await expect(page.getByRole('heading', {name: 'My projects', exact: true})).toHaveCount(0);
    await expect(page.getByRole('heading', {name: 'My projects', exact: true})).toBeVisible();
    expect(attempts).toHaveLength(2);
    expect(attempts[1] - attempts[0]).toBeGreaterThanOrEqual(990);
});

test('A newly saved server model appears immediately and opens as a separate private copy', {tag: '@critical'}, async ({page}) => {
    await signInWithQuota(page.request, `saved-project-${Date.now()}`);
    await page.goto('/projects');
    await expect(page.getByRole('heading', {name: 'No server versions saved yet', exact: true})).toBeVisible();
    await page.getByRole('button', {name: 'Create Ancient Dragon project', exact: true}).click();
    await page.getByRole('tab', {name: 'Export', exact: true}).click();
    await page.getByRole('button', {name: 'Save to server', exact: true}).click();
    await expect(page.getByText('Saved · config id:', {exact: false})).toBeVisible({timeout: 70000});
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    await page.getByRole('button', {name: 'Open saved version', exact: true}).click();
    await expect(page.locator('.structure-board > span')).toHaveCount(15);
    await page.getByRole('tab', {name: 'My projects', exact: true}).click();
    await expect(page.locator('[aria-label="Personal projects"] .project-row')).toHaveCount(2);
});
