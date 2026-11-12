import { defineConfig } from "@playwright/test";
import { allThemeModes, builtInThemeDefinitions } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { workspaceStyles } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles";
const evidence = process.env.OUTLINED_EVIDENCE ?? "outlined-report";
export default defineConfig({
  testDir: ".", testMatch: "outlined-fields.spec.ts", workers: 1, retries: 0,
  metadata: { outlinedCases: builtInThemeDefinitions.length * allThemeModes.length + workspaceStyles.length * 2 + 1 },
  outputDir: `${evidence}/results`,
  reporter: [["line"], ["json", { outputFile: `${evidence}/playwright.json` }], ["html", { outputFolder: `${evidence}/html`, open: "never" }]],
  use: { baseURL: "http://127.0.0.1:4197", viewport: { width: 1100, height: 1000 }, trace: "retain-on-failure" },
  webServer: { command: "pnpm exec vite --config vite.config.ts --port 4197 --strictPort", url: "http://127.0.0.1:4197", reuseExistingServer: false }
});
