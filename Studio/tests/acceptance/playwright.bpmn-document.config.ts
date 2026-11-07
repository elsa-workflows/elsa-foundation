import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL || !process.env.ELSA_WORKBENCH_URL || !process.env.UX_ACCEPTANCE_OUT)
  throw new Error("Complete BPMN authoring qualification requires the disposable composed-host runner.");
const output = process.env.UX_ACCEPTANCE_OUT;
export default defineConfig({
  testDir: ".", testMatch: ["bpmnDocumentEditors.spec.ts", "bpmnDocumentActivityGraphs.spec.ts"], fullyParallel: false, workers: 1, retries: 0, forbidOnly: true, timeout: 240_000,
  outputDir: `${output}/results`, reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  projects: [
    { name: "desktop-light", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } },
    { name: "desktop-dark", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "dark" } },
    { name: "narrow-desktop-light", use: { viewport: { width: 1100, height: 900 }, colorScheme: "light" } }
  ]
});
