import { createRequire } from "node:module";
import { writeFile } from "node:fs/promises";

const require = createRequire(new URL("../../../Studio/package.json", import.meta.url));
const { chromium } = require("@playwright/test");
const base = process.env.STUDIO_COMPOSITION_BASE;
const marker = process.env.STUDIO_COMPOSITION_MARKER;
const hostId = process.env.STUDIO_COMPOSITION_HOST_ID;
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  const errors = [], missing = [], routes = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("response", response => {
    const path = new URL(response.url()).pathname;
    if (response.status() >= 400 && (path === "/studio-runtime.js" || path.startsWith("/studio/") || path.startsWith("/_content/"))) {
      missing.push(`${response.status()} ${path}`);
    }
  });
  for (const path of ["/", "/qualification/custom"]) {
    await page.goto(base + path);
    await page.getByRole("heading", { name: "Acme module", level: 2, exact: true }).waitFor({ state: "visible", timeout: 30000 });
    await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Acme module", exact: true }).waitFor({ state: "visible" });
    const observed = await page.getByTestId("acme-module-marker").textContent();
    if (observed !== marker) throw new Error(`Custom contribution marker mismatch: ${observed}`);
    const runtime = await page.evaluate(() => window.__ELSA_STUDIO_RUNTIME__ ?? null);
    if (runtime?.hostId !== hostId) throw new Error("Custom host runtime configuration mismatch");
    routes.push({ path, marker: observed, hostId: runtime.hostId });
  }
  const shared = await page.evaluate(async () => {
    const react = await import("react");
    const direct = await import("/studio/vendor/react.js");
    const sdk = await import("@elsa-workflows/studio-sdk");
    const response = await fetch("/_elsa/studio/modules").then(result => result.json());
    const manifest = response.modules.find(module => module.id === "Acme.Studio.Composition");
    if (!manifest) throw new Error("Custom module is missing from real discovery");
    const module = await import(manifest.entry);
    return { sameReact: react.default === direct.default, sdk: typeof sdk.createHttpClient === "function",
      register: typeof module.register === "function", marker: module.moduleMarker, version: manifest.version };
  });
  if (!shared.sameReact || !shared.sdk || !shared.register || shared.marker !== marker || errors.length || missing.length) {
    throw new Error(JSON.stringify({ shared, errors, missing }));
  }
  const themeName = process.env.STUDIO_COMPOSITION_THEME_NAME;
  if (themeName) {
    const themeId = process.env.STUDIO_COMPOSITION_THEME_ID;
    if (themeId) {
      await page.waitForFunction(expected => document.documentElement.getAttribute("data-theme") === expected, themeId);
    }
    const title = await page.getByRole("button", { name: "Select theme", exact: true }).getAttribute("title");
    if (!title?.includes(themeName)) throw new Error("Restarted browser did not apply the persisted default theme");
    await page.getByRole("button", { name: "Select theme", exact: true }).click();
    const themeOption = page.getByRole("menuitemradio", { name: themeName, exact: true });
    await themeOption.waitFor({ state: "visible" });
    if (await themeOption.getAttribute("aria-checked") !== "true") throw new Error("Restarted browser did not select the persisted theme colors");
  }
  await page.screenshot({ path: process.env.STUDIO_COMPOSITION_OUTPUT.replace(/\.json$/, ".png"), fullPage: true });
  const result = { browserVersion: browser.version(), routes, shared, errors, missing, themeName: themeName ?? null };
  await writeFile(process.env.STUDIO_COMPOSITION_OUTPUT, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result));
} finally {
  await browser.close();
}
