import { defineConfig, devices } from "@playwright/test";

// Own the fixture server; never attach to a developer server or another checkout.
export default defineConfig({
  testDir: ".",
  testMatch: "dashboard-checkboxes.spec.ts",
  reporter: "line",
  use: { baseURL: "http://127.0.0.1:4198", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "pnpm exec vite --config vite.config.ts --port 4198 --strictPort",
    url: "http://127.0.0.1:4198",
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe"
  }
});
