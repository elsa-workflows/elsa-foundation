import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: ".", testMatch: "outlined-fields.spec.ts", workers: 1, retries: 0,
  reporter: [["line"], ["html", { outputFolder: process.env.OUTLINED_EVIDENCE ?? "outlined-report", open: "never" }]],
  use: { baseURL: "http://127.0.0.1:4197", viewport: { width: 1100, height: 1000 }, trace: "retain-on-failure" },
  webServer: { command: "pnpm exec vite --config vite.config.ts --port 4197 --strictPort", url: "http://127.0.0.1:4197", reuseExistingServer: false }
});
