import { createRequire } from "node:module";
import { writeFile } from "node:fs/promises";

const require = createRequire(new URL("../../../Studio/package.json", import.meta.url));
const { chromium } = require("@playwright/test");
const base = process.env.STUDIO_HOSTING_ASSET_BASE;
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  const errors = [], missing = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("response", response => {
    const path = new URL(response.url()).pathname;
    if (response.status() >= 400 && (path.startsWith("/studio/") || path.startsWith("/_content/"))) missing.push(`${response.status()} ${path}`);
  });
  for (const path of ["/", "/overview"]) {
    await page.goto(base + path);
    await page.getByRole("heading", { name: "Home", level: 2, exact: true }).waitFor({ state: "visible", timeout: 30000 });
    await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).waitFor({ state: "visible" });
  }
  const shared = await page.evaluate(async () => {
    const react = await import("react");
    const direct = await import("/studio/vendor/react.js");
    const sdk = await import("@elsa-workflows/studio-sdk");
    const ui = await import("@elsa-workflows/studio-ui");
    return { react: react.default === direct.default, sdk: typeof sdk.createHttpClient === "function", ui: Object.keys(ui).length > 0 };
  });
  if (errors.length || missing.length || !Object.values(shared).every(Boolean)) throw new Error(JSON.stringify({ errors, missing, shared }));
  await page.screenshot({ path: process.env.STUDIO_HOSTING_ASSET_OUTPUT.replace(/\.json$/, ".png"), fullPage: true });
  const result = { browserVersion: browser.version(), rootAndDeepLink: true, actualHomeModuleRoute: true, shared, errors, missing };
  await writeFile(process.env.STUDIO_HOSTING_ASSET_OUTPUT, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result));
} finally {
  await browser.close();
}
