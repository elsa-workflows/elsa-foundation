import { defineConfig } from "@playwright/test";
import { allThemeModes, builtInThemeDefinitions } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { workspaceStyles } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles";
const evidence = process.env.OUTLINED_EVIDENCE ?? "outlined-report";
const port = Number(process.env.OUTLINED_PORT ?? "4197");
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error("Use an owned unprivileged Outlined gallery port.");
const baseURL = `http://127.0.0.1:${port}`;
export default defineConfig({
  testDir: ".", testMatch: "outlined-fields.spec.ts", workers: 1, retries: 0,
  metadata: { outlinedCases: builtInThemeDefinitions.length * allThemeModes.length + workspaceStyles.length * 2 + 1, outlinedBaseURL: baseURL },
  outputDir: `${evidence}/results`,
  reporter: [["line"], ["json", { outputFile: `${evidence}/playwright.json` }], ["html", { outputFolder: `${evidence}/html`, open: "never" }]],
  use: { baseURL, viewport: { width: 1100, height: 1000 }, trace: "retain-on-failure" },
  webServer: { command: `pnpm exec vite --config vite.config.ts --port ${port} --strictPort`, url: baseURL, reuseExistingServer: false }
});
