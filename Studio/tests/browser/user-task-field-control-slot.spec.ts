import { expect, test } from "@playwright/test";

test("a module-owned field control is authored, previewed, saved, reopened, and published through the shared Slot", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html?custom-control");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await page.getByRole("textbox", { name: "Form ID" }).fill("ratings");
  await page.getByRole("textbox", { name: "Name" }).fill("Rating form");
  await page.getByRole("button", { name: "Rating", exact: true }).click();
  await page.getByRole("spinbutton", { name: "Maximum rating" }).fill("7");

  const tabs = page.getByRole("tablist", { name: "Form workspace" });
  const source = page.locator("[data-studio-code-editor] .cm-content");
  await tabs.getByRole("tab", { name: "Source" }).click();
  await expect(source).toContainText('"controlId": "example.rating/1"');
  await expect(source).toContainText('"configuration": {\n    "max": 7\n  }');
  await tabs.getByRole("tab", { name: "Preview" }).click();
  const preview = page.getByRole("region", { name: "Recipient preview" });
  await expect(preview.locator('input[type="number"]')).toHaveAttribute("max", "7");
  await page.screenshot({ path: testInfo.outputPath("field-control-preview-desktop.png"), animations: "disabled" });

  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  const row = page.getByRole("row").filter({ hasText: "ratings" });
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  await expect(source).toContainText('"controlId": "example.rating/1"');
  await expect(source).toContainText('"max": 7');

  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.getByTestId("published-form")).toContainText('"controlId":"example.rating/1"');
  await expect(page.getByTestId("published-form")).toContainText('"max":7');

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("tab", { name: "Preview", exact: true }).click();
  await expect(page.getByRole("region", { name: "Recipient preview" }).locator('input[type="number"]')).toHaveAttribute("max", "7");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("field-control-preview-narrow.png"), animations: "disabled" });
});

test("missing control modules preserve JSON and block publication", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/form-designer.html?missing-control");
  const row = page.getByRole("row").filter({ hasText: "contact" });
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await page.getByRole("button", { name: "Select Rating", exact: true }).click();

  await expect(page.locator(".user-task-designer__properties")).toContainText("Field control “example.rating/1” is unavailable");
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.locator(".user-task-designer__properties")).toContainText("Field control “example.rating/1” is unavailable");
  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string }[] }).formRequests);
  expect(requests.some(request => request.path === "/user-tasks/forms")).toBe(false);
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  const source = page.locator("[data-studio-code-editor] .cm-content");
  await expect(source).toContainText('"controlId": "example.rating/1"');
  await expect(source).toContainText('"max": 5');
  await page.screenshot({ path: testInfo.outputPath("field-control-unavailable-narrow.png"), animations: "disabled" });
});