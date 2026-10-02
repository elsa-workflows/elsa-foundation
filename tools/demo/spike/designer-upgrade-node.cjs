// Spike only: in Studio, opens a workflow, selects its first node, and uses the inspector's "Change exact version" to move
// the node to another catalog version of its activity, then reports which inputs the inspector shows. Screenshots go to
// OUT_DIR. Nothing is saved unless the editor autosaves (it does by default; the draft changes, the published workflow
// does not).
//
//   node tools/demo/spike/designer-upgrade-node.cjs STUDIO_DIR OUT_DIR WORKFLOW_NAME TARGET_VERSION
const path = require("path");
const fs = require("fs");
const [studioDir, outDir, workflowName, targetVersion] = process.argv.slice(2);
const { chromium } = require(path.join(path.resolve(studioDir), "node_modules/.pnpm/playwright@1.61.1/node_modules/playwright"));
const root = path.resolve(__dirname, "../../..");
const features = JSON.parse(fs.readFileSync(path.join(root, "artifacts/spike/hosts/wb/shells.json"))).CShells.Shells.default.Features;
const seed = features.FoundationIdentityAspNetCoreIdentityEntityFrameworkCore;
const studio = process.env.SPIKE_STUDIO_URL || "http://localhost:7221";

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const shot = async name => { await page.screenshot({ path: path.join(outDir, `${name}.png`) }); console.log(`  screenshot ${name}.png`); };
  try {
    await page.goto(`${studio}/workflows/definitions`);
    await page.waitForSelector("#username", { timeout: 30000 });
    await page.fill("#username", seed.SeedAdminUserName);
    await page.fill("#password", seed.SeedAdminPassword);
    await page.click("button[type=submit]");
    await page.waitForURL(url => url.toString().startsWith(studio), { timeout: 30000 });
    await page.waitForTimeout(3000);
    await page.getByText(workflowName, { exact: true }).first().click({ timeout: 15000 });
    await page.waitForTimeout(3500);
    await page.locator(".react-flow__node").first().click();
    await page.waitForTimeout(1000);
    await page.getByRole("tab", { name: "Version" }).first().click();
    await page.waitForTimeout(800);
    await page.getByRole("button", { name: /Change exact version/ }).first().click();
    await page.waitForTimeout(1500);
    await shot("01-change-version-dialog");
    const option = page.getByText(targetVersion, { exact: false });
    console.log(`  versions offered containing ${targetVersion}: ${await option.count()}`);
  } catch (error) {
    console.error(String(error));
    await shot("99-error");
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
})();
