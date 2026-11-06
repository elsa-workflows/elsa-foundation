import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("Run through tools/ux-acceptance/run.py with its owned disposable hosts.");
}
const output = process.env.UX_ACCEPTANCE_OUT;
const feature = process.env.UX_ACCEPTANCE_FEATURE ?? "baseline";
if (feature !== "baseline" && feature !== "jobs") throw new Error("Unknown mandatory acceptance inventory.");
export default defineConfig({
  testDir: ".",
  testMatch: feature === "jobs" ? "jobs.spec.ts" : "journeys.spec.ts",
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
    { name: "narrow-light", use: { viewport: { width: feature === "jobs" ? 390 : 360, height: 900 }, colorScheme: "light" } },
    { name: "narrow-dark", use: { viewport: { width: feature === "jobs" ? 390 : 360, height: 900 }, colorScheme: "dark" } }
  ]
});
