import { expect, test, type Page } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";

const shots = resolve("/opt/cursor/artifacts/screenshots");

async function shot(page: Page, name: string) {
  await mkdir(shots, { recursive: true });
  await page.screenshot({
    path: resolve(shots, `${name}.png`),
    type: "png",
    animations: "disabled"
  });
}

async function open(page: Page, url: string) {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto(url);
}

test.describe("classic rough edges after shots", () => {
  test.use({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });

  for (const theme of ["light", "dark"] as const) {
    test(`definitions list ${theme}`, async ({ page }) => {
      await open(page, `/?theme=${theme}&mode=classic-rough-edges&view=definitions`);
      await expect(page.getByRole("table", { name: "Workflow definitions" })).toBeVisible();
      await expect(page.getByText("Published").first()).toBeVisible();
      await expect(page.getByText("Newer draft")).toBeVisible();
      await expect(page.getByText("Draft", { exact: true })).toBeVisible();
      await shot(page, `02-definitions-after-${theme}`);
    });

    test(`runs list ${theme}`, async ({ page }) => {
      await open(page, `/?theme=${theme}&mode=classic-rough-edges&view=runs`);
      await expect(page.getByText("Order fulfillment")).toBeVisible();
      await expect(page.getByText("14LSBmpoVN")).toBeVisible();
      const current = page.locator('.nav-section a[aria-current="page"]');
      await expect(current).toHaveCount(1);
      await expect(current).toHaveText("Runs");
      await shot(page, `03-runs-after-${theme}`);
    });

    test(`nav exact-segment ${theme}`, async ({ page }) => {
      await open(page, `/?theme=${theme}&mode=classic-rough-edges&view=nav`);
      const current = page.locator('.nav-section a[aria-current="page"]');
      await expect(current).toHaveCount(1);
      await expect(current).toHaveText("Runs");
      await expect(page.locator('a[aria-label="Workflows"]')).not.toHaveClass(/active/);
      await shot(page, `04-nav-after-${theme}`);
    });

    test(`inspector tabs ${theme}`, async ({ page }) => {
      await open(page, `/?theme=${theme}&mode=activity-inspector-tabs`);
      const inspector = page.getByRole("complementary", { name: "Activity inspector" });
      await inspector.evaluate(element => { element.style.width = "320px"; element.style.height = "640px"; });
      const outerTabs = page.getByRole("tablist", { name: "Inspector panel tabs" });
      await expect(outerTabs.getByRole("tab", { name: "Inspector" })).toBeVisible();
      for (const name of ["Inspector", "Runtime", "Artifacts"] as const) {
        const tab = outerTabs.getByRole("tab", { name });
        await expect(tab).toHaveAttribute("title", name);
        const label = tab.locator("span").last();
        expect(await label.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      }
      await shot(page, `05-inspector-after-${theme}`);
    });
  }
});
