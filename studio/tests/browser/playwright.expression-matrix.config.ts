import { defineConfig, devices } from "@playwright/test";

const matrixPort = 4181;
const matrixUrl = `http://127.0.0.1:${matrixPort}`;

export default defineConfig({
  testDir: ".",
  testMatch: [
    "expression-code-intelligence.spec.ts",
    "expression-code-intelligence-accessibility.spec.ts"
  ],
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  timeout: 60_000,
  use: {
    baseURL: matrixUrl,
    trace: "retain-on-failure"
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "firefox", use: { ...devices["Desktop Firefox"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
    {
      name: "chromium-touch",
      testMatch: "expression-code-intelligence-accessibility.spec.ts",
      use: { ...devices["Pixel 7"] }
    }
  ],
  webServer: {
    command: [
      "pnpm exec vite build --config vite.config.ts",
      `pnpm exec vite preview --config vite.config.ts --host 127.0.0.1 --port ${matrixPort} --strictPort`
    ].join(" && "),
    url: matrixUrl,
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe"
  }
});
