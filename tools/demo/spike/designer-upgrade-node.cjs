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
  page.on("response", response => {
    const url = response.url();
    if (/\/design\//.test(url) && response.request().method() !== "GET")
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
    const dialog = page.getByRole("dialog").last();
    const buttons = await dialog.getByRole("button").allInnerTexts();
    console.log(`  dialog buttons: ${buttons.map(text => text.trim()).filter(Boolean).join(" | ")}`);
    const apply = dialog.getByRole("button", { name: /^(Apply|Change|Confirm|Update)/ }).last();
    if (await apply.count()) {
      await apply.scrollIntoViewIfNeeded();
      console.log(`  apply button disabled: ${await apply.isDisabled()}`);
      const invalid = await dialog.evaluate(element => {
        const form = element.querySelector("form");
        return form ? [...form.elements].filter(control => control.willValidate && !control.checkValidity())
          .map(control => `${control.tagName.toLowerCase()}[name=${control.getAttribute("name")}]: ${control.validationMessage}`) : ["no form"];
      });
      console.log(`  form controls failing validation: ${invalid.join(" | ") || "(none)"}`);
      const box = await apply.boundingBox();
      const hit = await page.evaluate(({ x, y }) => {
        const element = document.elementFromPoint(x, y);
        return element ? `${element.tagName.toLowerCase()}.${element.className} "${(element.textContent || "").trim().slice(0, 40)}"` : "nothing";
      }, { x: box.x + box.width / 2, y: box.y + box.height / 2 });
      console.log(`  element at the apply button's centre: ${hit}`);
      await page.evaluate(() => {
        window.__spikeEvents = [];
        for (const type of ["pointerdown", "mousedown", "pointerup", "mouseup", "click", "submit"])
          document.addEventListener(type, event => window.__spikeEvents.push(`${type}:${event.target.tagName?.toLowerCase()}${event.defaultPrevented ? "(prevented)" : ""}`), true);
      });
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.mouse.down();
      const moved = await apply.boundingBox();
      console.log(`  button box before mousedown y=${box.y.toFixed(0)}, after y=${moved?.y.toFixed(0)}`);
      await page.mouse.up();
      await page.waitForTimeout(500);
      console.log(`  events: ${(await page.evaluate(() => window.__spikeEvents)).join(", ")}`);
      const closed = await page.getByRole("dialog").waitFor({ state: "detached", timeout: 8000 }).then(() => true, () => false);
      console.log(`  a pointer click at the button closed the dialog: ${closed}`);
      if (!closed) {
        await apply.focus();
        await page.keyboard.press("Enter");
        const closedBySyntheticClick = await page.getByRole("dialog").waitFor({ state: "detached", timeout: 5000 }).then(() => true, () => false);
        console.log(`  focusing the button and pressing Enter closed the dialog: ${closedBySyntheticClick}`);
      }
      if (!(await page.getByRole("dialog").count()) === false) {
        console.log(`  the dialog is still open after a click; alerts: ${(await page.getByRole("alert").allInnerTexts()).join(" | ") || "(none)"}; submitting its form`);
        await dialog.evaluate(element => element.querySelector("form").requestSubmit());
        await page.getByRole("dialog").waitFor({ state: "detached", timeout: 15000 }).catch(async () =>
          console.log(`  still open; alerts: ${(await page.getByRole("alert").allInnerTexts()).join(" | ") || "(none)"}`));
      }
      await page.waitForTimeout(1000);
      await shot("02-after-change");
      await page.getByRole("tab", { name: "Inputs" }).first().click();
      await page.waitForTimeout(1000);
      const shown = await page.locator("body").innerText();
      console.log(`  inspector shows Tags after the change: ${shown.includes("Tags (comma-separated)") ? "yes" : "no"}`);
      await shot("03-inputs-after-change");
    }
  } catch (error) {
    console.error(String(error));
    await shot("99-error");
    process.exitCode = 1;
  } finally {
    await browser.close();
  }
})();
