import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.ELSA_WEAVER_OWNED_URL;
if (!baseURL) throw new Error("Set ELSA_WEAVER_OWNED_URL to the explicitly owned qualification host; this suite never starts or changes a host.");
for (const ownedURL of [baseURL, process.env.ELSA_WEAVER_OWNED_BACKEND_URL ?? baseURL]) {
  const address = new URL(ownedURL);
  if (!["localhost", "127.0.0.1", "[::1]"].includes(address.hostname) || ["7030", "7243"].includes(address.port)) {
    throw new Error("Weaver composed qualification requires separate owned local ports, not developer hosts.");
  }
}

export default defineConfig({
  testDir: ".", testMatch: "weaver-contributions.spec.ts", fullyParallel: false, workers: 1,
  reporter: "line", timeout: 60000,
  use: { baseURL, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }]
});
