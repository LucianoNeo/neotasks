import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e', timeout: 45000, workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: process.env.NEOTASKS_URL || 'http://127.0.0.1:8080', headless: true, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
});
