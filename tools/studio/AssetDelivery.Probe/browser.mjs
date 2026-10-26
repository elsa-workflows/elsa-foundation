import { createRequire } from "node:module";
import { writeFile } from "node:fs/promises";

// Automation can use Node. The independent consumer restore/publish/server run with denial shims instead.
const require = createRequire(new URL("../../../Studio/package.json", import.meta.url));
const { chromium } = require("@playwright/test");
const base = process.env.STUDIO_ASSET_PROBE_BASE;
const imports = JSON.parse(process.env.STUDIO_ASSET_PROBE_IMPORTS);
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  const failures = [];
  page.on("pageerror", error => failures.push(error.message));
  await page.goto(`${base}/__asset-qualification`);
  await page.addScriptTag({ type: "importmap", content: JSON.stringify({ imports }) });
  const result = await page.evaluate(async () => {
    const react = await import("react");
    const directReact = await import("/studio/vendor/react.js");
    const sdk = await import("@elsa-workflows/studio-sdk");
    const module = await import("/_content/Elsa.Studio.Dashboard/studio/modules/dashboard/module.js");
    const navigation = [], routes = [], widgets = [];
    module.register({ navigation: { add: x => navigation.push(x) }, routes: { add: x => routes.push(x) }, homeWidgets: { add: x => widgets.push(x) } });
    if (react.default !== directReact.default || typeof sdk.createHttpClient !== "function" || navigation.length !== 1 || routes.length !== 3 || widgets.length !== 6)
      throw new Error("Packed import-map/SDK/module registration failed");
    return { sharedReact: true, sdk: true, module: true, navigation: navigation.map(x => x.path), routes: routes.map(x => x.path), widgets: widgets.map(x => x.id) };
  });
  if (failures.length) throw new Error(failures.join("\n"));
  await writeFile(process.env.STUDIO_ASSET_PROBE_OUTPUT, JSON.stringify({ browserVersion: browser.version(), nodeVersion: process.version, ...result }, null, 2));
  console.log(JSON.stringify(result));
} finally {
  await browser.close();
}
