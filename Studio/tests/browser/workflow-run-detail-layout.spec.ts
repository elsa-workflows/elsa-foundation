import { expect, test } from "@playwright/test";

test("run detail fills the shell cell and switches responsive inspector modes", async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 800 });
  await page.goto("/?theme=light&mode=run-detail");

  const workbench = page.getByTestId("run-workbench");
  const canvas = page.getByRole("region", { name: "Workflow run canvas" });
  const inspector = page.getByRole("complementary", { name: "Run details" });
  const consolePanel = page.locator(".browser-run-console");
  await expect(workbench).toHaveAttribute("data-layout-mode", "desktop");
  await expect(inspector).toBeVisible();

  const [desktopWorkbenchBox, desktopCanvasBox, desktopInspectorBox, consoleBox] = await Promise.all([
    workbench.boundingBox(),
    canvas.boundingBox(),
    inspector.boundingBox(),
    consolePanel.boundingBox()
  ]);
  expect(desktopWorkbenchBox).not.toBeNull();
  expect(desktopCanvasBox?.width).toBeGreaterThanOrEqual(480);
  expect(desktopInspectorBox?.width).toBe(400);
  expect(Math.abs((desktopWorkbenchBox!.y + desktopWorkbenchBox!.height) - consoleBox!.y)).toBeLessThanOrEqual(1);

  await page.setViewportSize({ width: 700, height: 800 });
  await expect(workbench).toHaveAttribute("data-layout-mode", "medium");
  await expect(inspector).toBeHidden();
  await page.getByRole("button", { name: "Select activity" }).click();
  await expect(inspector).toBeVisible();
  await expect(inspector).toHaveCSS("position", "absolute");

  await page.getByRole("button", { name: "Close details" }).click();
  await page.setViewportSize({ width: 460, height: 800 });
  await expect(workbench).toHaveAttribute("data-layout-mode", "narrow");
  await expect(canvas).toBeVisible();
  await expect(inspector).toBeHidden();
  await page.getByRole("button", { name: "Select activity" }).click();
  await expect(canvas).toBeHidden();
  await expect(inspector).toBeVisible();
});
