import base from '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio/playwright.config';
export default {
  ...base,
  testDir: '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio/tests/browser',
  testMatch: /(?:minimal-studio|workspace-appearance|workflow-templates|workflow-run-detail-layout|workflow-run-status-contrast|workflow-run-deletion|editor-run-canvas|bpmn-decision)\.spec\.ts/,
  outputDir: '/tmp/elsa-2898-main-3e93-qualification/browser-output',
  workers: 2,
  reporter: [['json', { outputFile: '/tmp/elsa-2898-main-3e93-qualification/browser-results.json' }], ['line']],
  use: { ...base.use, baseURL: 'http://127.0.0.1:4207' },
  webServer: {
    ...base.webServer,
    command: 'pnpm exec vite --config tests/browser/vite.config.ts --port 4207 --strictPort',
    cwd: '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio',
    url: 'http://127.0.0.1:4207',
    reuseExistingServer: false
  }
};
