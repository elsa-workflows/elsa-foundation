import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import axe from "axe-core";

const screenshotDir = resolve(process.cwd(), process.env.SETTINGS_SCREENSHOT_DIR ?? "../docs/pr-assets/settings-shell");

async function expectNoSeriousAxeViolations(page: import("@playwright/test").Page) {
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector(".settings-shell");
    if (!root) return [{ id: "missing-settings-shell", impact: "critical", help: "Settings shell root was not in the document.", nodes: [] }];
    const result = await (window as Window & {
      axe: { run: (context: Element, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: unknown[] }> }> };
    }).axe.run(root, { resultTypes: ["violations"] });
    return result.violations.filter(violation => violation.impact === "critical" || violation.impact === "serious");
  });
  await test.info().attach("axe-critical-serious.json", {
    body: JSON.stringify(violations, null, 2),
    contentType: "application/json"
  });
  expect(violations).toEqual([]);
}

test.describe("settings shell", () => {
  test.use({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });

  test("captures the pre-S4 Classic light empty /settings route when asked", async ({ page }) => {
    test.skip(!process.env.SETTINGS_SCREENSHOTS || !process.env.SETTINGS_BEFORE, "Before screenshots are captured once from main.");
    await mkdir(screenshotDir, { recursive: true });
    await page.goto("/?mode=settings-before");
    await expect(page.getByText("No Studio route is registered for /settings.")).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "before-classic-light.png"), fullPage: false });
  });

  test("opens /settings with a section list, Appearance, and a sidebar Settings entry", async ({ page }) => {
    await page.goto("/settings/appearance?mode=settings");
    await expect(page.getByRole("navigation", { name: "Settings" }).getByRole("link", { name: "Settings" })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Administration" })).toHaveCount(0);
    await expect(page.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Appearance" })).toHaveAttribute("aria-current", "page");
    await expect(page.getByRole("heading", { name: "Appearance" })).toBeVisible();
    await expect(page.getByRole("radiogroup", { name: "Colour mode" }).getByRole("radio", { name: /System/ })).toBeVisible();
    await expect(page.getByRole("radiogroup", { name: "Theme" }).getByRole("radio", { name: /Classic/ })).toBeVisible();
    await expect(page.getByRole("link", { name: "Open Theme Builder" })).toBeVisible();
    await page.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Modules" }).click();
    await expect(page).toHaveURL(/\/settings\/modules/);
    await expect(page.getByRole("heading", { name: "Modules" })).toBeVisible();
  });

  test("follows a live OS colour-scheme change in System mode", async ({ page }) => {
    await page.emulateMedia({ colorScheme: "light" });
    await page.goto("/settings/appearance?mode=settings&appearance=system");
    await page.getByRole("radiogroup", { name: "Colour mode" }).getByRole("radio", { name: /System/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "light");
    await page.emulateMedia({ colorScheme: "dark" });
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
  });

  test("has no critical or serious automated accessibility violations", async ({ page }) => {
    await page.goto("/settings/appearance?mode=settings");
    await expect(page.getByRole("heading", { name: "Appearance" })).toBeVisible();
    await expectNoSeriousAxeViolations(page);
  });

  test("moves through Appearance radios with the keyboard", async ({ page }) => {
    await page.goto("/settings/appearance?mode=settings");
    const system = page.getByRole("radiogroup", { name: "Colour mode" }).getByRole("radio", { name: /System/ });
    await system.focus();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByRole("radiogroup", { name: "Colour mode" }).getByRole("radio", { name: "Light" })).toBeFocused();
    await expect(page.getByRole("radiogroup", { name: "Colour mode" }).getByRole("radio", { name: "Light" })).toHaveAttribute("aria-checked", "true");
  });

  test("captures after screenshots when asked", async ({ page }) => {
    test.skip(!process.env.SETTINGS_SCREENSHOTS, "Screenshot capture is opt-in.");
    await mkdir(screenshotDir, { recursive: true });

    await page.goto("/settings/appearance?mode=settings");
    await expect(page.getByRole("heading", { name: "Appearance" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-light.png"), fullPage: false });

    await page.goto("/settings/appearance?mode=settings&appearance=dark");
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
    await expect(page.getByRole("heading", { name: "Appearance" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-dark.png"), fullPage: false });
  });
});
