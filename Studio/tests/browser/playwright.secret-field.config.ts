import { defineConfig, devices } from "@playwright/test";

const port = Number(process.env.SECRET_PROTOTYPE_PORT ?? 4187);
const output = process.env.SECRET_PROTOTYPE_OUTPUT ?? "test-results/secret-field-prototype";
export default defineConfig({
  testDir: ".",
  testMatch: ["secret-field-prototype.spec.ts", "secret-scopes.spec.ts"],
  fullyParallel: false,
  workers: 2,
  retries: 0,
  timeout: 60_000,
  outputDir: `${output}/results`,
  reporter: [["line"], ["json", { outputFile: `${output}/playwright.json` }], ["html", { outputFolder: `${output}/html`, open: "never" }]],
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    cwd: decodeURIComponent(new URL("../..", import.meta.url).pathname),
    command: `pnpm exec vite --config tests/browser/vite.config.ts --port ${port}`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: false,
    stdout: "pipe", stderr: "pipe"
  }
});
