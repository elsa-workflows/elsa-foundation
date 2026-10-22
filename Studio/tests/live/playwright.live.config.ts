import { defineConfig, devices } from "@playwright/test";

// Opt-in journeys against a running Studio and Workbench. They author and run real workflows, so they never run in
// the mocked suite; set ELSA_STUDIO_URL (for example http://localhost:5089) to run them.
export default defineConfig({
  testDir: ".",
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  timeout: 180_000,
  use: {
    baseURL: process.env.ELSA_STUDIO_URL,
    trace: "retain-on-failure",
    viewport: { width: 1440, height: 900 }
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 900 } } }]
});
