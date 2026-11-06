import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("Run through tools/ux-acceptance/run.py with its owned disposable hosts.");
}
const output = process.env.UX_ACCEPTANCE_OUT;
const suite = process.env.UX_ACCEPTANCE_SUITE ?? "all";
if (!["all", "existing", "standalone-triggers"].includes(suite)) throw new Error("Unknown finite acceptance suite");
const baseMatch = suite === "existing" ? "journeys.spec.ts" : suite === "standalone-triggers" ? "standaloneTriggers.spec.ts" : /(?:journeys|standaloneTriggers)\.spec\.ts/;
export default defineConfig({
  testDir: ".",
  testMatch: baseMatch,
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
    { name: "narrow-light", use: { viewport: { width: 360, height: 900 }, colorScheme: "light" } },
    { name: "narrow-dark", use: { viewport: { width: 360, height: 900 }, colorScheme: "dark" } },
    ...(suite === "existing" ? [] : [
      { name: "compact-light", testMatch: "standaloneTriggers.spec.ts", use: { viewport: { width: 320, height: 900 }, colorScheme: "light" as const } },
      { name: "desktop-dim", testMatch: "standaloneTriggers.spec.ts", metadata: { themeMode: "dim" }, use: { viewport: { width: 1440, height: 1000 }, colorScheme: "dark" as const } },
      { name: "desktop-high-contrast", testMatch: "standaloneTriggers.spec.ts", metadata: { themeMode: "high-contrast" }, use: { viewport: { width: 1440, height: 1000 }, colorScheme: "dark" as const } }
    ])
  ]
});
