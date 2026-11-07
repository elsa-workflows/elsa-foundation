import { defineConfig } from "@playwright/test";

if (process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.ELSA_STUDIO_URL
  || !process.env.ELSA_WORKBENCH_URL || !process.env.UX_ACCEPTANCE_OUT) {
  throw new Error("The BPMN prototype proof requires the owned disposable Studio/Workbench runner.");
}

const output = process.env.UX_ACCEPTANCE_OUT;
export default defineConfig({
  testDir: ".",
  testMatch: "bpmnJsPrototype.spec.ts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 240_000,
  outputDir: `${output}/results`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: process.env.ELSA_STUDIO_URL, trace: "on", screenshot: "only-on-failure" },
  projects: [{ name: "desktop-light", use: { viewport: { width: 1440, height: 1000 }, colorScheme: "light" } }]
});
