import { defineConfig, devices } from "@playwright/test";

const performancePort = 4180;
const performanceUrl = `http://127.0.0.1:${performancePort}`;

export default defineConfig({
  testDir: ".",
  testMatch: "expression-code-intelligence-performance.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  timeout: 180_000,
  use: {
    baseURL: performanceUrl,
    trace: "retain-on-failure"
  },
  projects: [
    { name: "chromium-production", use: { ...devices["Desktop Chrome"] } }
  ],
  webServer: {
    command: [
      "pnpm exec vite build --config vite.config.ts",
      `pnpm exec vite preview --config vite.config.ts --host 127.0.0.1 --port ${performancePort} --strictPort`
    ].join(" && "),
    url: performanceUrl,
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe"
  }
});
