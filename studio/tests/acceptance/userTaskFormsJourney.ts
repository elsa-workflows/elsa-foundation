import { expect, type Page, type TestInfo } from "@playwright/test";
import { confirmNewResource } from "../newResourceDialog";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

export async function userTaskFormsJourney(page: Page, info: TestInfo, capture: Capture) {
  const operate = page.locator(".sidebar").getByRole("navigation", { name: "Operate", exact: true });
  const toggle = operate.getByRole("button", { name: "Operate", exact: true });
  if (await toggle.getAttribute("aria-expanded") === "false") await toggle.click();

  await operate.getByRole("link", { name: "User tasks", exact: true }).click();
  await expect(page).toHaveURL(/\/user-tasks(?:\?|$)/);
  await expect(page.getByRole("heading", { name: "No tasks here", exact: true })).toBeVisible();
  await expect(page.getByText(/Task records are read-only on this backend/)).toHaveCount(0);
  await capture(page, info, "task-inbox");

  await operate.getByRole("link", { name: "Task forms", exact: true }).click();
  await expect(page).toHaveURL(/\/user-tasks\/forms(?:\?|$)/);
  await page.getByRole("button", { name: "New form", exact: true }).first().click();
  const formId = `approval-${info.project.name}-${Date.now()}`;
  const title = `Approval form ${info.project.name}`;
  await confirmNewResource(page, "form", title);
  const showPanel = async (label: string) => {
    const expand = page.getByRole("button", { name: `Expand ${label} panel`, exact: true });
    if (await expand.isVisible()) await expand.click();
  };
  await showPanel("properties");
  await page.getByLabel("Form ID", { exact: true }).fill(formId);
  await showPanel("controls");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await showPanel("properties");
  await page.getByLabel("Key", { exact: true }).fill("Note");
  await page.getByLabel("Label", { exact: true }).fill("Review note");
  await page.getByLabel("Required", { exact: true }).check();
  await capture(page, info, "form-authoring");

  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.locator(".studio-toast").filter({ hasText: "Form published" })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/user-tasks/forms/${formId}(?:\\?|$)`));
  await page.reload();
  await page.getByRole("tab", { name: "Versions", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Published version 1", exact: true })).toBeVisible();
  await expect(page.getByLabel("Published form versions preview", { exact: true }).getByText("Review note", { exact: true })).toBeVisible();
  await capture(page, info, "published-form");
}
