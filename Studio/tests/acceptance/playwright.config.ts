import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("Run through tools/ux-acceptance/run.py with its owned disposable hosts.");
}
const output = process.env.UX_ACCEPTANCE_OUT;
export default defineConfig({
  testDir: ".",
  testMatch: ["journeys.spec.ts", "run-history-timeline.spec.ts"],
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  outputDir: `${output}/results`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  projects: [
    { name: "desktop-light", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } },
    { name: "desktop-dark", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "dark" } },
    { name: "narrow-light", testMatch: "journeys.spec.ts", use: { viewport: { width: 360, height: 900 }, colorScheme: "light" } },
    { name: "narrow-dark", testMatch: "journeys.spec.ts", use: { viewport: { width: 360, height: 900 }, colorScheme: "dark" } },
    { name: "timeline-320-light", testMatch: "run-history-timeline.spec.ts", use: { viewport: { width: 320, height: 900 }, colorScheme: "light" } },
    { name: "timeline-320-dark", testMatch: "run-history-timeline.spec.ts", use: { viewport: { width: 320, height: 900 }, colorScheme: "dark" } }
  ]
});
