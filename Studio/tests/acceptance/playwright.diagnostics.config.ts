import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.UX_ACCEPTANCE_OUT || !process.env.UX_ACCEPTANCE_DIAGNOSTICS_DIR)
  throw new Error("Run diagnostics through the owned sequential acceptance runner.");
const output = process.env.UX_ACCEPTANCE_OUT;
export default defineConfig({
  testDir: ".", testMatch: "run-history-diagnostics.spec.ts", fullyParallel: false, workers: 1, retries: 0, forbidOnly: true,
  timeout: 600_000, outputDir: `${output}/results`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  projects: [
    { name: "diagnostics-desktop-light", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } },
    { name: "diagnostics-320-dark", use: { viewport: { width: 320, height: 900 }, colorScheme: "dark" } }
  ]
});
