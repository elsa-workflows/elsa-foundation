import { expect, test } from "@playwright/test";

test("native task form designer supports recipient preview and accessible editing at desktop and phone widths", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await expect(page.getByRole("complementary", { name: "Field palette", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Form canvas", exact: true })).toBeVisible();
  await expect(page.getByRole("complementary", { name: "Field properties", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("button", { name: "Number", exact: true }).click();
  await expect(page.locator(".user-task-designer__advanced")).not.toHaveAttribute("open");
  await page.getByRole("button", { name: "Select Text field 1", exact: true }).click();
  await page.getByRole("textbox", { name: "Label", exact: true }).fill("Customer note");
  await page.screenshot({ path: testInfo.outputPath("form-designer-desktop.png") });

  await page.getByRole("tab", { name: "Preview", exact: true }).click();
  const preview = page.getByRole("region", { name: "Recipient preview", exact: true });
  await expect(preview.getByRole("textbox", { name: "Customer note", exact: true })).toBeVisible();
  await preview.getByRole("textbox", { name: "Customer note", exact: true }).fill("preview-only response");
  await page.getByRole("tab", { name: "Designer", exact: true }).click();
  await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "text1,number1");

  const secondHandle = page.getByRole("button", { name: "Reorder Number field 1", exact: true });
  const secondField = page.locator(".user-task-designer__field").nth(1);
  await secondHandle.focus();
  await page.keyboard.press("Space");
  await expect(secondField).toHaveClass(/is-dragging/);
  const initialY = (await secondField.boundingBox())!.y;
  await page.keyboard.press("ArrowUp");
  await expect.poll(async () => (await secondField.boundingBox())?.y ?? Number.POSITIVE_INFINITY).toBeLessThan(initialY);
  await page.keyboard.press("Space");
  await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "number1,text1");
  await page.screenshot({ path: testInfo.outputPath("form-designer-keyboard-order.png") });

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole("complementary", { name: "Field palette", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("form-designer-phone.png") });
});

test("native task form designer reorders fields by pointer drag", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("button", { name: "Number", exact: true }).click();
  const first = page.getByRole("button", { name: "Reorder Text field 1", exact: true });
  const target = page.locator(".user-task-designer__field").nth(1);
  const sourceBounds = (await first.boundingBox())!;
  const targetBounds = (await target.boundingBox())!;
  await page.mouse.move(sourceBounds.x + sourceBounds.width / 2, sourceBounds.y + sourceBounds.height / 2);
  await page.mouse.down();
  await page.mouse.move(sourceBounds.x + sourceBounds.width / 2, sourceBounds.y + sourceBounds.height / 2 + 10, { steps: 3 });
  await page.mouse.move(targetBounds.x + targetBounds.width / 2, targetBounds.y + targetBounds.height / 2, { steps: 10 });
  await page.mouse.up();
  await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "number1,text1");
});