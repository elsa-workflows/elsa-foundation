// Spike only: drives Studio in a headless Chromium (Playwright from the Studio checkout) to show what the designer shows.
// Signs in with the backend's development admin, then for each workflow named: opens it in the designer, selects its
// first node, and reports which inputs the inspector shows and which exact activity version the node is pinned to. It also
// reports whether the palette lists "Add note". Screenshots go to OUT_DIR.
//
//   node tools/demo/spike/designer-check.cjs STUDIO_DIR OUT_DIR WORKFLOW_NAME...
//
// The user and password are read from the Workbench spike host's shells.json (its development seed); nothing is printed.
// SPIKE_TRACE=1 also lists the backend calls Studio makes.
const path = require("path");
const fs = require("fs");
const [studioDir, outDir, ...workflowNames] = process.argv.slice(2);
const { chromium } = require(path.join(path.resolve(studioDir), "node_modules/.pnpm/playwright@1.61.1/node_modules/playwright"));
const root = path.resolve(__dirname, "../../..");
const features = JSON.parse(fs.readFileSync(path.join(root, "artifacts/spike/hosts/wb/shells.json"))).CShells.Shells.default.Features;
const seed = features.FoundationIdentityAspNetCoreIdentityEntityFrameworkCore;
const studio = process.env.SPIKE_STUDIO_URL || "http://localhost:7221";

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  page.on("response", response => {
    const url = response.url();
    if (process.env.SPIKE_TRACE && /\/design\/|\/publishing\//.test(url))
      console.log(`  ${response.request().method()} ${url.replace(/^https?:\/\/[^/]+/, "")} -> ${response.status()}`);
  });
  const shot = async name => { await page.screenshot({ path: path.join(outDir, `${name}.png`) }); console.log(`  screenshot ${name}.png`); };
  try {
    await page.goto(`${studio}/workflows/definitions`);
    await page.waitForSelector("#username", { timeout: 30000 });
    await page.fill("#username", seed.SeedAdminUserName);
    await page.fill("#password", seed.SeedAdminPassword);
    await page.click("button[type=submit]");
    await page.waitForURL(url => url.toString().startsWith(studio), { timeout: 30000 });
    await page.waitForTimeout(3000);
    for (const [index, name] of workflowNames.entries()) {
      const slug = `${String(index + 1).padStart(2, "0")}-${name.replace(/[^a-z0-9.]+/gi, "-")}`;
      await page.goto(`${studio}/workflows/definitions`);
      await page.waitForTimeout(2500);
      await page.getByText(name, { exact: true }).first().click({ timeout: 15000 });
      await page.waitForTimeout(3500);
      console.log(`workflow '${name}'`);
      const search = page.getByPlaceholder("Search activities").first();
      if (await search.count()) {
        await search.fill("note");
        await page.waitForTimeout(800);
        await shot(`${slug}-palette`);
        await search.fill("");
      }
      await page.locator(".react-flow__node").first().click();
      await page.waitForTimeout(1500);
      const shown = await page.locator("body").innerText();
      const inputs = ["Text", "Tags (comma-separated)"].filter(label => shown.includes(`\n${label}\n`) || shown.includes(`${label}\nString`));
      console.log(`  inspector inputs shown: ${inputs.join(", ") || "(none recognized)"}`);
      await shot(`${slug}-inputs`);
      await page.getByRole("tab", { name: "Version" }).first().click();
      await page.waitForTimeout(1000);
      const versionText = await page.locator("body").innerText();
      const exact = /EXACT VERSION\s*\n\s*([0-9.]+)/i.exec(versionText);
      console.log(`  node pinned to exact version: ${exact ? exact[1] : "?"}`);
      await shot(`${slug}-version`);
    }
  } catch (error) {
    console.error(String(error));
    await shot("99-error");
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
})();
