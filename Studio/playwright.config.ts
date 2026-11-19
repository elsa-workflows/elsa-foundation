import { defineConfig, devices } from "@playwright/test";

const browserPort = Number(process.env.STUDIO_BROWSER_PORT ?? 4179);

export default defineConfig({
  testDir: "./tests/browser",
  // The performance benchmark owns a production server, serial worker, CPU throttle, and extended
  // timeout in playwright.performance.config.ts. Running it in the parallel development-server suite
  // makes its cold-page samples contend with unrelated browser cases and invalidates the measurement.
  testIgnore: ["expression-code-intelligence-performance.spec.ts", "weaver-contributions.spec.ts"],
  fullyParallel: true,
  reporter: "line",
  use: {
    baseURL: `http://127.0.0.1:${browserPort}`,
    trace: "retain-on-failure"
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } }
  ],
  webServer: {
    command: "pnpm exec vite --config tests/browser/vite.config.ts",
    url: `http://127.0.0.1:${browserPort}`,
    reuseExistingServer: !process.env.CI,
    stdout: "pipe",
    stderr: "pipe"
  }
});
