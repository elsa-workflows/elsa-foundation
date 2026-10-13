import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";

const screenshotDir = resolve(process.cwd(), process.env.NAV_PLACEMENT_SCREENSHOT_DIR ?? "../docs/pr-assets/nav-placement");

test.describe("navigation placement", () => {
  test.use({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });

  test("keeps Home, Workflows and Runs on main and hides More tools until opened", async ({ page }) => {
    await page.goto("/?mode=nav-placement");
    await expect(page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Home" })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Workflows" })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Runs" })).toBeVisible();
    await expect(page.getByRole("button", { name: "More tools" })).toHaveAttribute("aria-expanded", "false");
    await expect(page.getByRole("link", { name: "Executables" })).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Administration" }).getByRole("link", { name: "Modules" })).toBeVisible();
  });

  test("expands More tools and keeps admin items in the gated Administration group", async ({ page }) => {
    await page.goto("/?mode=nav-placement&more=open");
    await expect(page.getByRole("button", { name: "More tools" })).toHaveAttribute("aria-expanded", "true");
    await expect(page.getByRole("link", { name: "Executables" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Activity definitions" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Secrets" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Diagnostics", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Administration" }).getByRole("link", { name: "Features" })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Administration" }).getByRole("link", { name: "Theme builder" })).toBeVisible();
  });

  test("captures Classic light screenshots when asked", async ({ page }) => {
    test.skip(!process.env.NAV_PLACEMENT_SCREENSHOTS, "Screenshot capture is opt-in.");
    await mkdir(screenshotDir, { recursive: true });

    await page.goto("/?mode=nav-placement");
    await expect(page.getByRole("navigation", { name: "Main" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-more-collapsed.png"), fullPage: false });

    await page.goto("/?mode=nav-placement&more=open");
    await expect(page.getByRole("link", { name: "Executables" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-more-expanded.png"), fullPage: false });

    await page.goto("/?mode=nav-before");
    await expect(page.getByRole("navigation", { name: "Workspace" }).getByRole("link", { name: "Dashboard" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "before.png"), fullPage: false });
  });
});
