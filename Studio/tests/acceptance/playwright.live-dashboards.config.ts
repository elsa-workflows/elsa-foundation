import { defineConfig } from "@playwright/test";

if (process.env.UX_DASHBOARD_LIVE_OWNED !== "1" || process.env.UX_ACCEPTANCE_OWNED !== "1" ||
    !process.env.ELSA_STUDIO_URL || !process.env.UX_BACKEND_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("Use the opt-in Dashboard live qualification runner with its owned hosts. This profile contacts the configured real provider.");
}

const output = process.env.UX_ACCEPTANCE_OUT;
const restart = process.env.UX_DASHBOARD_RESTART === "1";
export default defineConfig({
  testDir: ".",
  testMatch: "liveDashboards.spec.ts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  maxFailures: 1,
  forbidOnly: true,
  timeout: 600_000,
  outputDir: `${output}/${restart ? "restart-results" : "results"}`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright${restart ? "-restart" : ""}.json` }], ["html", { outputFolder: `${output}/html${restart ? "-restart" : ""}`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  // Inference runs once. The journeys capture saved native views in all four appearances without another model turn.
  projects: [{ name: "live-desktop", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } }]
});
