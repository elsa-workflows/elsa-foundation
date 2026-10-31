import { defineConfig, devices } from "@playwright/test";

// Own this fixture server: reusing another checkout's default port can test unrelated source.
export default defineConfig({
  testDir: ".",
  testMatch: /(?:workflow-library|workflow-definition-browse-context|workflow-definition-move|workflow-folder-restructure)\.spec\.ts/,
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  use: { baseURL: "http://127.0.0.1:4196", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "pnpm exec vite --config vite.config.ts --port 4196 --strictPort",
    url: "http://127.0.0.1:4196",
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe"
  }
});
