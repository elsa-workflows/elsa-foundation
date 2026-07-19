import { expect, test } from "@playwright/test";

test("workflow definition moves use a native, keyboard-accessible destination dialog", async ({ page }) => {
  await page.setViewportSize({ width: 430, height: 760 });
  await page.goto("/?theme=light&mode=move-definitions");

  const trigger = page.getByRole("button", { name: "Move to folder" });
  await trigger.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Move workflow definitions" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("radio", { name: "Unfiled" })).toBeChecked();
  await expect(dialog.getByRole("radio", { name: /Operations/ })).toBeVisible();
  await expect(dialog.getByRole("tree")).toHaveCount(0);

  const operations = dialog.getByRole("radio", { name: /Operations/ });
  await operations.focus();
  await page.keyboard.press("Space");
  await expect(operations).toBeChecked();
  await dialog.getByRole("button", { name: "Move" }).focus();
  await page.keyboard.press("Enter");
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("status")).toHaveText("Moved 1 workflow definition");
  await expect(trigger).toBeFocused();
});
