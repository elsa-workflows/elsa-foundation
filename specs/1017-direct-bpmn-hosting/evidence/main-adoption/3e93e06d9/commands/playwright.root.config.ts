import base from '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio/tests/browser/playwright.workflow-library.config';
export default {
  ...base,
  testDir: '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio/tests/browser',
  testMatch: /(?:workflow-library|workflow-definition-browse-context|workflow-definition-move|workflow-folder-restructure|workflow-canvas-styles|adaptive-graph-ports|activity-definition-graph-authoring|instance-live-canvas)\.spec\.ts/,
  outputDir: '/tmp/elsa-2898-main-3e93-qualification/browser-root-output',
  reporter: [['json', { outputFile: '/tmp/elsa-2898-main-3e93-qualification/browser-root-results.json' }], ['line']],
  use: { ...base.use, baseURL: 'http://127.0.0.1:4208' },
  webServer: {
    ...base.webServer,
    cwd: '/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio/tests/browser',
    command: 'pnpm exec vite --config vite.config.ts --port 4208 --strictPort',
    url: 'http://127.0.0.1:4208'
  }
};
