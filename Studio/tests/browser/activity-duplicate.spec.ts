import { expect, test } from "@playwright/test";
import axe from "axe-core";

test("duplicates the selected activity from the context menu with a fresh name", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate&theme=light");

  const original = page.getByRole("button", { name: "Notify buyer", exact: true });
  await expect(original).toBeVisible();
  await original.click({ button: "right" });

  const menu = page.getByRole("menu", { name: "Activity actions" });
  await expect(menu).toBeVisible();
  await expect(menu.getByRole("menuitem", { name: /Duplicate/ })).toBeVisible();
  await menu.getByRole("menuitem", { name: /Duplicate/ }).click();

  await expect(page.getByRole("status")).toHaveText("Duplicated 1 activity.");
  const copy = page.getByRole("button", { name: "Notify buyer (copy)", exact: true });
  await expect(copy).toBeVisible();
  await expect(original).toBeVisible();
  const originalBox = await original.boundingBox();
  const copyBox = await copy.boundingBox();
  expect(copyBox!.x).toBeGreaterThan(originalBox!.x);
  expect(copyBox!.y).toBeGreaterThan(originalBox!.y);
  await expect(copy).toHaveAttribute("aria-pressed", "true");
});

test("copies and pastes with Ctrl/Cmd shortcuts and ignores them in a text field", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate&theme=dark");

  await page.getByRole("button", { name: "Notify buyer", exact: true }).click();
  await page.keyboard.press("Control+c");
  await expect(page.getByRole("status")).toHaveText("Copied 1 activity.");
  await page.keyboard.press("Control+v");
  await expect(page.getByRole("status")).toHaveText("Pasted 1 activity.");
  await expect(page.getByRole("button", { name: "Notify buyer (copy)", exact: true })).toBeVisible();

  await page.getByLabel("Display name").focus();
  await page.keyboard.press("Control+d");
  await expect(page.getByRole("button", { name: "Notify buyer (copy 2)", exact: true })).toHaveCount(0);

  await page.getByRole("button", { name: "Notify buyer", exact: true }).click();
  await page.keyboard.press("Control+d");
  await expect(page.getByRole("button", { name: "Notify buyer (copy 2)", exact: true })).toBeVisible();
});

test("activity duplicate menu has no critical or serious automated accessibility violations", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate&theme=light");
  await page.getByRole("button", { name: "Notify buyer", exact: true }).click({ button: "right" });
  await expect(page.getByRole("menu", { name: "Activity actions" })).toBeVisible();

  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string }> }> };
    }).axe.run(document, { resultTypes: ["violations"] });
    return result.violations.filter(violation =>
      violation.impact === "critical" || violation.impact === "serious"
    );
  });
  expect(violations).toEqual([]);
});
