import { confirmNewResource } from "../newResourceDialog";
import { expect, test } from "@playwright/test";

type FormRequest = { path: string; body: { form?: { id?: string; name?: string }; revision?: number } };

const draftRequests = async (page: import("@playwright/test").Page) => page.evaluate(() =>
  (window as unknown as { formRequests: FormRequest[] }).formRequests.filter(request => request.path.endsWith("/draft"))
);

test("enables autosave by default without writing when opening an unchanged publication", async ({ page }) => {
  await page.goto("/form-designer.html");
  const row = page.getByRole("table", { name: "Task forms" }).locator("tbody tr").first();
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();

  await expect(page.getByRole("checkbox", { name: "Auto-save" })).toBeChecked();
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveValue("Contact form");
  await page.waitForTimeout(1400);
  expect(await draftRequests(page)).toHaveLength(0);
});

test("debounces draft saves, serializes revisions, and keeps editing during a slow save", async ({ page }) => {
  await page.goto("/form-designer.html?save-delay=1800");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("button", { name: "Form settings", exact: true }).click();

  const id = page.getByRole("textbox", { name: "Form ID", exact: true });
  const name = page.getByRole("textbox", { name: "Name", exact: true });
  await id.fill("autosaved");
  await name.fill("First draft");
  await expect(page.getByText("Saving draft", { exact: true })).toBeVisible({ timeout: 5000 });
  await expect(name).toBeEnabled();
  await name.fill("Latest draft");

  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible({ timeout: 10000 });
  await expect(name).toHaveValue("Latest draft");
  const selectedField = page.getByRole("button", { name: "Select Text field 1", exact: true });
  await expect(selectedField).toHaveAttribute("aria-pressed", "true");

  await expect.poll(async () => (await draftRequests(page)).length).toBe(2);
  const requests = await draftRequests(page);
  expect(requests.map(request => request.body.revision)).toEqual([0, 1]);
  expect(requests.map(request => request.body.form?.name)).toEqual(["First draft", "Latest draft"]);
});

test("pauses autosave after an error and allows an explicit retry", async ({ page }) => {
  await page.goto("/form-designer.html?save-fails");
  const row = page.getByRole("table", { name: "Task forms" }).locator("tbody tr").first();
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  const name = page.getByRole("textbox", { name: "Name", exact: true });
  await name.fill("Unsaved after failure");

  await expect(page.getByRole("alert")).toContainText("Draft save failed in fixture.");
  await expect(page.getByText("Save failed", { exact: true })).toBeVisible();
  await name.fill("Still local");
  await page.waitForTimeout(1500);
  expect(await draftRequests(page)).toHaveLength(1);

  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Draft save failed in fixture.");
  expect(await draftRequests(page)).toHaveLength(2);
  await expect(name).toHaveValue("Still local");
});

test("turning autosave off keeps edits local until an explicit save", async ({ page }) => {
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("checkbox", { name: "Auto-save" }).uncheck();
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("manual-only");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Manual only");
  await page.waitForTimeout(1400);
  expect(await draftRequests(page)).toHaveLength(0);

  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  expect(await draftRequests(page)).toHaveLength(1);
});

test("Cancel flushes pending autosave changes before leaving the editor", async ({ page }) => {
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("flush-on-leave");
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Saved on cancel");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();

  await expect(page.getByRole("heading", { name: "Task forms" })).toBeVisible();
  expect(await draftRequests(page)).toHaveLength(1);
  await expect(page.getByRole("row").filter({ hasText: "flush-on-leave" })).toContainText("Draft");
});

test("unapplied JSON source edits are not autosaved", async ({ page }) => {
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("textbox", { name: "Form ID", exact: true }).fill("source-pending");
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  const source = page.locator("[data-studio-code-editor] .cm-content");
  await source.fill(JSON.stringify({ id: "source-pending", version: "1", name: "Pending JSON", provider: "elsa.basic-forms/1", fields: [] }, null, 2));

  await expect(page.getByText(/Unapplied source edits/)).toBeVisible();
  await page.waitForTimeout(1400);
  expect(await draftRequests(page)).toHaveLength(0);
});

test("guards host and browser navigation while there are unsaved local edits", async ({ page }) => {
  await page.goto("/form-designer.html");
  const row = page.getByRole("table", { name: "Task forms" }).locator("tbody tr").first();
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Pending change");

  const prevented = await page.evaluate(() => {
    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
  expect(prevented).toBe(true);
});