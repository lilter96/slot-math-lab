import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './unit', fullyParallel: true, workers: 2, reporter: 'list', timeout: 10000 });
