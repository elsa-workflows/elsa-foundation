import { expect, test, type Locator } from "@playwright/test";

const statusValues = ["Autosaving...", "Autosaved", "Saving before review...", "Published to an unusually long publication channel", ""];

for (const width of [320, 768, 1024, 1440]) {
  test(`keeps workflow actions stationary through save status changes at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 720 });
    await page.goto("/?mode=workflow-toolbar&theme=dark&name=A%20long%20workflow%20definition%20name%20that%20must%20not%20crowd%20the%20actions");
    const toolbar = page.locator(".wf-editor-toolbar");
    const buttons = toolbar.getByRole("button");
    const initial = await readButtonPositions(buttons);
    const initialStatus = await toolbar.getByRole("status").boundingBox();
    expect(initial.every(box => box.x >= 0 && box.x + box.width <= width)).toBe(true);

    for (const value of statusValues) {
      await page.getByLabel("Save status").selectOption(value);
      await expect(toolbar.getByRole("status")).toHaveText(value);
      expect(await toolbar.getByRole("status").boundingBox()).toEqual(initialStatus);
      expect(await readButtonPositions(buttons)).toEqual(initial);
    }
    await expect(toolbar.getByRole("button", { name: "Review & publish" })).toBeVisible();
  });
}

test("makes secondary actions keyboard accessible and preserves autosave state", async ({ page }) => {
  await page.goto("/?mode=workflow-toolbar");
  const trigger = page.getByRole("button", { name: "More workflow actions" });
  await trigger.focus();
  await trigger.press("ArrowDown");
  const menu = page.getByRole("menu", { name: "More workflow actions" });
  const autosave = menu.getByRole("menuitemcheckbox", { name: /Autosave/ });
  await expect(autosave).toBeFocused();
  await expect(autosave).toHaveAttribute("aria-checked", "true");
  await autosave.press("Space");
  await expect(autosave).toHaveAttribute("aria-checked", "false");
  await autosave.press("ArrowDown");
  await expect(menu.getByRole("menuitem", { name: "Propose update" })).toBeFocused();
  await expect(menu.getByRole("menuitem", { name: "Review risks" })).toBeDisabled();
  await expect(menu.getByRole("menuitem", { name: "Export artifact" })).toBeDisabled();
  await expect(menu.getByRole("menuitem", { name: "Import BPMN" })).toBeVisible();
  await menu.getByRole("menuitem", { name: "Propose update" }).press("ArrowDown");
  await expect(menu.getByRole("menuitem", { name: "Export JSON" })).toBeFocused();
  await menu.getByRole("menuitem", { name: "Export JSON" }).press("Enter");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(page.getByText("Selected action: Export JSON")).toBeVisible();

  await trigger.click();
  await expect(autosave).toHaveAttribute("aria-checked", "false");
  await autosave.press("Escape");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();

  await trigger.click();
  await autosave.press("Tab");
  await expect(menu).toHaveCount(0);
  await expect(page.getByLabel("Save status")).toBeFocused();
  await trigger.press("ArrowUp");
  await expect(menu.getByRole("menuitem", { name: "Export BPMN" })).toBeFocused();
  await menu.getByRole("menuitem", { name: "Export BPMN" }).press("Shift+Tab");
  await expect(menu).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Run", exact: true })).toBeFocused();
  await trigger.click();
  await page.getByLabel("Save status").click();
  await expect(menu).toHaveCount(0);
});

function readButtonPositions(buttons: Locator) {
  return buttons.evaluateAll(elements => elements.map(element => {
    const { x, y, width, height } = element.getBoundingClientRect();
    return { x, y, width, height };
  }));
}

test("keeps the overflow menu inside a narrow viewport and primary commands direct", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 600 });
  await page.goto("/?mode=workflow-toolbar&theme=dark");
  const toolbar = page.locator(".wf-editor-toolbar");
  for (const name of ["Save", "Review & publish", "Run"]) {
    await toolbar.getByRole("button", { name, exact: true }).click();
    await expect(page.getByText(`Selected action: ${name}`, { exact: true })).toBeVisible();
  }
  await toolbar.getByRole("button", { name: "More workflow actions" }).click();
  const menu = page.getByRole("menu", { name: "More workflow actions" });
  await expect(menu).toBeVisible();
  const box = await menu.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  expect(box!.y + box!.height).toBeLessThanOrEqual(600);
});
