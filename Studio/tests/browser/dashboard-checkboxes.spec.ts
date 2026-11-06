import { expect, test, type Locator, type Page } from "@playwright/test";

/** A real down/up pair catches focus-induced movement that DOM click() cannot reproduce. */
async function toggleWithPointer(page: Page, checkbox: Locator) {
  await checkbox.scrollIntoViewIfNeeded();
  const before = await checkbox.boundingBox();
  if (!before) throw new Error("The checkbox must have a visible box.");
  const point = { x: before.x + before.width / 2, y: before.y + before.height / 2 };
  await page.mouse.move(point.x, point.y);
  await page.mouse.down();
  const afterFocus = await checkbox.boundingBox();
  if (!afterFocus) throw new Error("The checkbox must remain visible after pointer focus.");
  expect(Math.abs(afterFocus.y - before.y)).toBeLessThanOrEqual(1);
  expect(Math.abs(afterFocus.x - before.x)).toBeLessThanOrEqual(1);
  await page.mouse.up();
  await expect(checkbox).toBeChecked();
}

for (const width of [320, 360, 1280]) {
  test(`native dashboard checkboxes keep pointer targets while focus changes at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/dashboard-checkboxes.html");
    const grouping = page.getByRole("group", { name: "Group by", exact: true });
    const status = grouping.getByRole("checkbox", { name: "Current status", exact: true });
    const runKind = grouping.getByRole("checkbox", { name: "Run kind", exact: true });
    await status.check();
    await toggleWithPointer(page, runKind);
    await expect(page.getByLabel("Selected grouping fields")).toHaveText("status, runKind");
    await runKind.press("Space");
    await expect(runKind).not.toBeChecked();
    await expect(page.getByLabel("Selected grouping fields")).toHaveText("status");

    await page.getByRole("button", { name: "Open tile settings", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Tile presentation and parameters", exact: true });
    const category = dialog.getByRole("group", { name: "category (string)", exact: true }).getByRole("checkbox");
    const minimum = dialog.getByRole("group", { name: "minimum (integer)", exact: true }).getByRole("checkbox");
    await category.check();
    await toggleWithPointer(page, minimum);
    await page.getByRole("button", { name: "Use tile settings", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByLabel("Tile parameter overrides")).toHaveText('{"category":{"type":"string","literal":"retail"},"minimum":{"type":"integer","literal":"1"}}');
  });
}
