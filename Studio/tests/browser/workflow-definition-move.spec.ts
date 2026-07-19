import { expect, test } from "@playwright/test";

test("workflow definitions rebase a paged public capability view after a keyboard move", async ({ page }) => {
  await page.setViewportSize({ width: 430, height: 760 });
  await page.goto("/?theme=light&mode=move-definitions");

  await expect(page.getByText("First page workflow")).toBeVisible();
  await page.getByRole("button", { name: "Next" }).click();
  await expect(page.getByText("Second page workflow")).toBeVisible();
  await page.getByRole("checkbox", { name: "Select workflow definition Second page workflow" }).check();
  const trigger = page.getByRole("button", { name: "Move to folder" });
  await trigger.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Move workflow definitions" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("radio", { name: "Unfiled" })).toBeChecked();
  await expect(dialog.getByRole("radio", { name: /Operations/ })).toBeVisible();
  await expect(dialog.getByRole("tree")).toHaveCount(0);

  await dialog.getByRole("button", { name: "Move" }).focus();
  await page.keyboard.press("Enter");
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("status")).toHaveText("Moved 1 workflow definition");
  await expect(page.getByText("Moved workflow")).toBeVisible();
  await expect(page.getByText("Second page workflow")).toBeHidden();
  await expect(page.getByText("Page 1")).toBeVisible();
  await expect(page.getByText("1 selected")).toBeHidden();
  await expect(page.getByRole("button", { name: "Refresh" })).toBeFocused();
  await page.getByRole("button", { name: "Folders" }).click();
  await page.getByRole("treeitem", { name: "Unfiled" }).click();
  await expect(page.getByText("Moved workflow")).toBeVisible();
  await page.getByRole("button", { name: "Folders" }).click();
  await page.getByRole("treeitem", { name: /Operations/ }).click();
  await expect(page.getByText("Moved workflow")).toBeHidden();
  await expect(page.getByText("No active workflow definitions")).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as Window & { moveRequests?: { url: string; body: unknown }[] }).moveRequests)).toEqual([
    { url: "/browser/definition-placement", body: { definitionIds: ["definition-2"], folderId: null } }
  ]);
});

test("move failure keeps the full dialog reachable in a short viewport", async ({ page }) => {
  await page.setViewportSize({ width: 760, height: 430 });
  await page.goto("/?theme=light&mode=move-definitions&move=failure");

  await page.getByRole("checkbox", { name: "Select workflow definition First page workflow" }).check();
  await page.getByRole("button", { name: "Move to folder" }).click();
  const dialog = page.getByRole("dialog", { name: "Move workflow definitions" });
  await dialog.getByRole("button", { name: "Move" }).click();
  await expect(dialog.getByRole("alert")).toContainText("Couldn't move the selected workflow definitions");
  await expect(dialog.getByRole("button", { name: "Cancel" })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Move" })).toBeVisible();
  const box = await dialog.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.y).toBeGreaterThanOrEqual(0);
  expect(box!.y + box!.height).toBeLessThanOrEqual(430);
});
