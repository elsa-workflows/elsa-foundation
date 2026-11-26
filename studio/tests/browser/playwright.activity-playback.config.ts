import { defineConfig, devices } from "@playwright/test";
import { fileURLToPath } from "node:url";
const port = Number(process.env.PLAYBACK_FIXTURE_PORT ?? 4187);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error("Invalid owned playback fixture port");
export default defineConfig({
  testDir: ".", testMatch: "activity-playback.spec.ts", fullyParallel: false, workers: 1, retries: 0,
  reporter: [["line"], ["json", { outputFile: process.env.PLAYBACK_FIXTURE_REPORT ?? "/tmp/activity-playback-browser.json" }]],
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: { command: `pnpm exec vite --config tests/browser/vite.config.ts --port ${port}`, cwd: fileURLToPath(new URL("../../", import.meta.url)), url: `http://127.0.0.1:${port}/activity-playback.html`,
    reuseExistingServer: false, stdout: "pipe", stderr: "pipe" }
});
