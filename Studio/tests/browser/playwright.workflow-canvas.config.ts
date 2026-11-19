import { defineConfig } from "@playwright/test";
import library from "./playwright.workflow-library.config";

export default defineConfig({
  ...library,
  testMatch: /(?:workflow-canvas-styles|adaptive-graph-ports|activity-definition-graph-authoring|instance-live-canvas)\.spec\.ts/,
  use: { ...library.use, baseURL: "http://127.0.0.1:4197" },
  webServer: {
    command: "pnpm exec vite --config vite.config.ts --port 4197 --strictPort",
    url: "http://127.0.0.1:4197",
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe"
  }
});
