import { expect, test } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import axe from "axe-core";

const screenshotDir = resolve(process.cwd(), process.env.HOME_SCREENSHOT_DIR ?? "../docs/pr-assets/2758");

async function expectNoSeriousAxeViolations(page: import("@playwright/test").Page) {
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const root = document.querySelector(".home-page") ?? document.querySelector(".dashboard-page");
    if (!root) return [{ id: "missing-home-page", impact: "critical", help: "Home page root was not in the document.", nodes: [] }];
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

test.describe("customizable Home", () => {
  test.use({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 2
  });

  test("renders the default Home landing and passes keyboard plus axe checks", async ({ page }) => {
    page.on("pageerror", error => console.error(error));
    await page.goto("/?mode=home");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Recent workflows" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Recent runs" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Needs attention" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Tasks" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Create a workflow" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Open the designer" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Try Elsa locally" })).toBeVisible();
    await expect(page.getByText("Completed")).toBeVisible();
    await expect(page.getByText("All clear")).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Home" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Dashboard" })).toHaveCount(0);

    await page.getByRole("button", { name: "Edit home" }).focus();
    await expect(page.getByRole("button", { name: "Edit home" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("button", { name: "Add widget" }).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Reset to default" })).toBeVisible();
    const addWidget = page.getByRole("button", { name: "Add widget" }).first();
    await addWidget.focus();
    await expect(addWidget).toBeFocused();
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Add widget" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel("Search widgets")).toBeFocused();
    await expect(dialog.getByRole("button", { name: "Tasks" })).toHaveCount(0);
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(addWidget).toBeFocused();

    await page.getByRole("button", { name: "Done" }).click();

    await expectNoSeriousAxeViolations(page);
  });

  test("captures Classic light and dark before/after screenshots when asked", async ({ page }) => {
    test.skip(!process.env.HOME_SCREENSHOTS, "Screenshot capture is opt-in.");
    await mkdir(screenshotDir, { recursive: true });

    await page.goto("/?mode=home-before");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toHaveCount(0);
    await page.screenshot({ path: resolve(screenshotDir, "before-classic-light.png"), fullPage: false });

    await page.goto("/?mode=home-before&theme=dark");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Get started" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "before-classic-dark.png"), fullPage: false });

    await page.goto("/?mode=home");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-light.png"), fullPage: false });

    await page.goto("/?mode=home&theme=dark");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
    await page.screenshot({ path: resolve(screenshotDir, "after-classic-dark.png"), fullPage: false });
  });
});
