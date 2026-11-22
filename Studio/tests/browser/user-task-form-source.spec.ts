import { confirmNewResource } from "../newResourceDialog";
import { expect, test } from "@playwright/test";

test("Forms editor switches between canvas, JSON source, and recipient preview", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("textbox", { name: "Form ID" }).fill("source-contact");
  await page.getByRole("textbox", { name: "Name" }).fill("Source contact");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("textbox", { name: "Label", exact: true }).fill("Email address");

  const tabs = page.getByRole("tablist", { name: "Form workspace" });
  const canvasTab = tabs.getByRole("tab", { name: "Designer" });
  const sourceTab = tabs.getByRole("tab", { name: "Source" });
  const previewTab = tabs.getByRole("tab", { name: "Preview" });
  await expect(canvasTab).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: "Select Email address" })).toHaveAttribute("aria-pressed", "true");

  await sourceTab.click();
  await page.keyboard.press("ArrowRight");
  await expect(previewTab).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("ArrowLeft");
  await expect(sourceTab).toHaveAttribute("aria-selected", "true");

  const source = page.locator("[data-studio-code-editor] .cm-content");
  await expect(source).toContainText('"label": "Email address"');
  const updatedForm = {
    id: "source-contact",
    version: "1",
    name: "JSON contact",
    provider: "elsa.basic-forms/1",
    fields: [{ key: "email", label: "Email from JSON", kind: "text", required: true, readOnly: false, help: "Shared source" }]
  };
  await source.fill(JSON.stringify(updatedForm, null, 2));
  await expect(page.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Apply", exact: true }).click();

  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeEnabled();
  await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "email");

  await canvasTab.click();
  await expect(page.getByRole("button", { name: "Select Email from JSON" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("textbox", { name: "Help text" })).toHaveValue("Shared source");
  await page.getByRole("button", { name: "Form settings", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Name" })).toHaveValue("JSON contact");
  await previewTab.click();
  const preview = page.getByRole("region", { name: "Recipient preview" });
  await preview.getByRole("textbox", { name: "Email from JSON" }).fill("preview-only@example.test");
  await canvasTab.click();
  await sourceTab.click();
  await expect(source).not.toContainText("preview-only@example.test");

  await source.fill("{");
  await expect(page.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(page.locator(".user-task-designer__source-error")).toContainText(/valid JSON/i);
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Publish", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Reset", exact: true }).click();
  await expect(page.locator(".user-task-designer__source-error")).toHaveCount(0);
  await expect(source).toContainText('"label": "Email from JSON"');

  await page.screenshot({ path: testInfo.outputPath("form-source-preview-desktop.png"), animations: "disabled" });
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  await expect(source).toContainText('"label": "Email from JSON"');
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  const savedForm = page.getByRole("row").filter({ hasText: "source-contact" });
  await savedForm.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Edit", exact: true }).click();
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  await expect(source).toContainText('"label": "Email from JSON"');
});

test("legacy Forms publication is blocked while JSON source is unapplied", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html?legacy");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("textbox", { name: "Form ID" }).fill("legacy-source");
  await page.getByRole("textbox", { name: "Name" }).fill("Legacy source");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("tab", { name: "Source", exact: true }).click();
  const source = page.locator("[data-studio-code-editor] .cm-content");
  await source.fill(JSON.stringify({
    id: "legacy-source", version: "1", name: "Legacy source", provider: "elsa.basic-forms/1",
    fields: [{ key: "text1", label: "Unapplied label", kind: "text", required: false, readOnly: false }]
  }, null, 2));

  await expect(page.getByRole("button", { name: "Publish form version", exact: true })).toBeDisabled();
  await expect(page.getByText(/Unapplied source edits/)).toBeVisible();
  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string; body: unknown }[] }).formRequests);
  expect(requests).toHaveLength(0);
});

for (const appearance of ["light", "dark"] as const) {
  test(`source edits cannot change form identity or system-owned fields in narrow ${appearance} mode`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), appearance);
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await page.getByRole("button", { name: "Expand properties panel", exact: true }).click();
  await page.getByRole("textbox", { name: "Form ID" }).fill("immutable-form");
  await page.getByRole("textbox", { name: "Name" }).fill("Immutable form");
  await page.getByRole("button", { name: "Expand controls panel", exact: true }).click();
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("tab", { name: "Source" }).click();

  const source = page.locator("[data-studio-code-editor] .cm-content");
  const changedIdentity = {
    id: "other-form",
    version: "99",
    name: "Attempted change",
    provider: "other-provider",
    fields: [{ key: "text1", label: "Text field 1", kind: "text", required: false, readOnly: false }]
  };
  await source.fill(JSON.stringify(changedIdentity, null, 2));
  await page.getByRole("button", { name: "Apply", exact: true }).click();
  await expect(page.locator(".user-task-designer__source-error")).toContainText(/identifier.*cannot be changed|system-managed/i);
  await expect(page.getByRole("button", { name: "Reset", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Reset", exact: true }).click();
  const canvasTab = page.locator('.user-task-designer__view-tabs [data-tab-id="canvas"]');
  await expect(canvasTab).toBeVisible();
  await canvasTab.click();
  await page.getByRole("button", { name: "Expand properties panel", exact: true }).click();
  await expect(page.getByRole("region", { name: "Form canvas" })).toBeHidden();
  expect(await page.getByRole("complementary", { name: "Field properties" }).evaluate(element => element.getBoundingClientRect().width)).toBeGreaterThan(300);
  await page.getByRole("button", { name: "Form settings", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Form ID" })).toHaveValue("immutable-form");

  const requests = await page.evaluate(() => (window as unknown as { formRequests: { path: string; body: unknown }[] }).formRequests);
  expect(requests).toHaveLength(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath(`form-source-narrow-${appearance}.png`), animations: "disabled" });
  });
}