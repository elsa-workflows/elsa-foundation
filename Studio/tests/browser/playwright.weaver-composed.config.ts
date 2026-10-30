import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.ELSA_WEAVER_OWNED_URL;
const outputDir = process.env.ELSA_WEAVER_OWNED_ARTIFACTS ?? "../../../test-results/weaver-composed";
if (!baseURL) throw new Error("Set ELSA_WEAVER_OWNED_URL to the explicitly owned qualification host; this suite never starts or changes a host.");
for (const ownedURL of [baseURL, process.env.ELSA_WEAVER_OWNED_BACKEND_URL ?? baseURL]) {
  const address = new URL(ownedURL);
  if (!["localhost", "127.0.0.1", "[::1]"].includes(address.hostname) || ["7030", "7243"].includes(address.port)) {
    throw new Error("Weaver composed qualification requires separate owned local ports, not developer hosts.");
  }
}

export default defineConfig({
  testDir: ".", testMatch: "weaver-contributions.spec.ts", fullyParallel: false, workers: 1,
  outputDir,
  reporter: [["line"], ["json", { outputFile: `${outputDir}/report.json` }]], timeout: 60000,
  use: { baseURL, storageState: process.env.ELSA_WEAVER_OWNED_AUTH_STATE, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1920, height: 1080 } } }]
});
