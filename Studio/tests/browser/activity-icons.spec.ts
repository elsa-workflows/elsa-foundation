import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";

test("an extension catalog iconName reaches the palette and the canvas", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-icons");

  const fixture = page.getByTestId("activity-icons-fixture");
  await expect(fixture).toBeVisible();
  await expect(fixture.locator('.wf-activity-icon[data-icon="Rocket"]').first()).toBeVisible();
  await expect(fixture.locator('.wf-node[data-icon="Rocket"]').first()).toBeVisible();
});

test("without a catalog iconName the extension activity uses the category placeholder", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-icons&legacy=1");

  const fixture = page.getByTestId("activity-icons-fixture");
  await expect(fixture).toBeVisible();
  await expect(fixture.locator('.wf-activity-icon[data-icon="activity"]').first()).toBeVisible();
  await expect(fixture.locator('.wf-node[data-icon="activity"]').first()).toBeVisible();
  await expect(fixture.locator('.wf-activity-icon[data-icon="Rocket"]')).toHaveCount(0);
});

test("a host --studio-wf-activity-* override retints palette and node icons", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-icons&host-override=1");

  const fixture = page.getByTestId("activity-icons-fixture");
  await expect(fixture).toBeVisible();

  const terminal = fixture.locator('.wf-activity-icon[data-tone="terminal"]').first();
  const composite = fixture.locator('.wf-activity-icon[data-tone="composite"]').first();
  const nodeTerminal = fixture.locator('.wf-node[data-tone="terminal"] .wf-node-icon').first();

  await expect(terminal).toHaveCSS("color", "rgb(255, 17, 0)");
  await expect(composite).toHaveCSS("color", "rgb(124, 58, 237)");
  await expect(nodeTerminal).toHaveCSS("color", "rgb(255, 17, 0)");
});

test("capture Classic light before/after for PR assets", async ({ browser }) => {
  test.skip(!process.env.CAPTURE_PR_ASSETS, "set CAPTURE_PR_ASSETS to write screenshots");
  const outDir = resolve(process.env.CAPTURE_PR_ASSETS_DIR || "/tmp/pr-assets-activity-icons");
  await mkdir(outDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });
  const page = await context.newPage();

  await page.goto("/?mode=activity-icons&legacy=1", { waitUntil: "networkidle" });
  await expect(page.getByTestId("activity-icons-fixture")).toBeVisible();
  await expect(page.locator('.wf-activity-icon[data-icon="activity"]').first()).toBeVisible();
  await page.screenshot({ path: resolve(outDir, "before-classic-light.png"), animations: "disabled" });

  await page.goto("/?mode=activity-icons", { waitUntil: "networkidle" });
  await expect(page.getByTestId("activity-icons-fixture")).toBeVisible();
  await expect(page.locator('.wf-activity-icon[data-icon="Rocket"]').first()).toBeVisible();
  await expect(page.locator('.wf-activity-icon[data-icon="Rocket"] svg rect')).toHaveCount(0);
  await page.screenshot({ path: resolve(outDir, "after-classic-light.png"), animations: "disabled" });

  await context.close();
});
