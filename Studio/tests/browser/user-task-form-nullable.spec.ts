import { expect, test } from "@playwright/test";

test("opens, edits, saves, and publishes a form with nullable optional settings", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html?nullable-settings&custom-control");

  const rows = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Contact form");
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();

  const designer = page.locator(".user-task-designer");
  const field = page.getByRole("button", { name: "Select Accept terms", exact: true });
  await expect(field).toBeVisible();
  await field.click();
  await page.getByRole("textbox", { name: "Label", exact: true }).fill("Consent");

  await page.getByRole("tab", { name: "Source", exact: true }).click();
  const source = page.locator("[data-studio-code-editor] .cm-content");
  await expect(source).toContainText('"trueLabel": null');
  const json = JSON.parse(await source.innerText()) as { fields: { label: string }[] };
  json.fields[0].label = "Consent from source";
  await source.fill(JSON.stringify(json, null, 2));
  await expect(page.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Apply", exact: true }).click();
  await page.getByRole("tab", { name: "Designer", exact: true }).click();
  await expect(page.getByRole("button", { name: "Select Consent from source", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await expect(page.getByRole("button", { name: "Select Consent from source", exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  await expect(source).toContainText('"label": "Consent from source"');
  await expect(source).not.toContainText('"trueLabel": null');

  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.locator("output.user-task-version-value")).toHaveText("Published · version 2");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(rows.first()).toContainText("Published · v2");
  expect(await designer.count()).toBe(0);
});