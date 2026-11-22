import { expect, test } from "@playwright/test";

test("edits one form through shared drafts, read-only history, publication, and discard", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");

  const rows = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Contact form");
  await expect(rows.first()).toContainText("Published · v1");

  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  const name = page.getByRole("textbox", { name: "Name", exact: true });
  await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeDisabled();
  await name.fill("Contact draft");
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();

  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Published · Draft");
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await expect(name).toHaveValue("Contact draft");
  await name.fill("Contact version two");
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  await expect(page.getByRole("button", { name: "Select version 1", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Published form versions preview" })).toContainText("Published version 1");
  await page.screenshot({ path: testInfo.outputPath("form-lifecycle-draft-history.png") });

  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.locator("output.user-task-version-value")).toHaveText("Published · version 2");
  await expect(page.getByText("No draft saved", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Reusable task forms" })).toBeVisible();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Published · v2");

  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await name.fill("Discarded working change");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await expect(page.getByRole("button", { name: "Select Text field 1", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  page.once("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Discard draft", exact: true }).click();
  await expect(name).toHaveValue("Contact version two");
  await expect(page.getByRole("button", { name: "Select Text field 1", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Published · v2");

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(rows).toHaveCount(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("form-lifecycle-narrow-list.png") });
});

test("opens a reusable form when a non-control table cell is clicked", async ({ page }) => {
  await page.goto("/form-designer.html");

  const row = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr").first();
  await row.getByRole("cell", { name: "contact", exact: true }).click();

  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeVisible();
});

test("supports keyboard row navigation and keeps the action menu isolated from the row", async ({ page }) => {
  await page.goto("/form-designer.html");
  const row = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr").first();
  const actions = row.getByRole("button", { name: /^Actions for / });

  await actions.click();
  await expect(page.getByRole("menuitem", { name: "Edit", exact: true })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "Delete", exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveCount(0);
  await page.keyboard.press("Escape");
  await row.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(row.getByRole("button", { name: "Contact form", exact: true })).toBeFocused();
});

test("cancelling form deletion leaves the row and sends no request", async ({ page }) => {
  await page.goto("/form-designer.html");
  const rows = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");

  page.once("dialog", dialog => dialog.dismiss());
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Delete", exact: true }).click();

  await expect(rows).toHaveCount(1);
  await expect(rows.first().getByRole("button", { name: /^Actions for / })).toBeFocused();
  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string; body: unknown }[] }).formRequests);
  expect(requests.some(request => request.path.endsWith("/delete"))).toBe(false);
});

test("confirmed deletion uses the current revision and removes the row only after success", async ({ page }) => {
  await page.goto("/form-designer.html");
  const rows = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");

  page.once("dialog", dialog => dialog.accept());
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Delete", exact: true }).click();

  await expect(rows).toHaveCount(0);
  await expect(page.locator("header").getByRole("button", { name: "Create form", exact: true })).toBeFocused();
  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string; body: { revision: number } }[] }).formRequests);
  expect(requests).toContainEqual({ path: "/user-tasks/forms/contact/delete", body: { revision: 0 } });
});

test("omits deletion for older backends and read-only users while preserving view access", async ({ page }) => {
  await page.goto("/form-designer.html?delete-unavailable");
  let row = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr").first();
  await row.getByRole("button", { name: /^Actions for / }).click();
  await expect(page.getByRole("menuitem", { name: "Edit", exact: true })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "Delete", exact: true })).toHaveCount(0);

  await page.goto("/form-designer.html?read-only");
  row = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr").first();
  await row.getByRole("button", { name: /^Actions for / }).click();
  await expect(page.getByRole("menuitem", { name: "View", exact: true })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "Delete", exact: true })).toHaveCount(0);
  await page.getByRole("menuitem", { name: "View", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeDisabled();
});

test("retains the row and reports a failed deletion", async ({ page }) => {
  await page.goto("/form-designer.html?delete-fails");
  const rows = page.getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");

  page.once("dialog", dialog => dialog.accept());
  await rows.first().getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Delete", exact: true }).click();

  await expect(rows).toHaveCount(1);
  await expect(page.getByRole("alert")).toContainText("Delete failed in fixture.");
});

test("uses the advertised legacy forms contract for published form access and direct publication", async ({ page }) => {
  await page.goto("/form-designer.html?legacy");
  const rows = page.getByRole("region", { name: "Reusable forms" }).getByRole("table", { name: "Reusable task forms" }).locator("tbody tr");
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("Contact form");

  await rows.first().getByRole("button", { name: "Open form contact" }).click();
  await expect(page.getByRole("region", { name: "Published form", exact: true })).toContainText("Published version 1");
  await page.getByRole("navigation", { name: "Breadcrumb", exact: true }).getByRole("button", { name: "Forms", exact: true }).click();
  await page.getByRole("button", { name: "Create form version", exact: true }).click();
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("feedback");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Feedback form");
  await page.getByRole("button", { name: "Publish form version", exact: true }).click();
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(1)).toContainText("Feedback form");

  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string; body: unknown }[] }).formRequests);
  expect(requests).toHaveLength(1);
  expect(requests[0].path).toBe("/user-tasks/forms");
  expect(requests[0].body).toMatchObject({ id: "feedback", name: "Feedback form", version: "1", fields: [] });
});