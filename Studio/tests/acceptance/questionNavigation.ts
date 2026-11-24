import { expect, type Page } from "@playwright/test";

/** Selects an already mounted workspace panel without executing another query. */
export async function selectQuestionTab(page: Page, id: "build" | "preview") {
  await closeQuestionProperties(page);
  const workspace = page.locator(".named-question-workspace:visible");
  await expect(workspace).toHaveCount(1);
  const tab = workspace.locator(`[role="tab"][data-tab-id="${id}"]`);
  if (await tab.getAttribute("aria-selected") !== "true") await tab.click();
}

export async function openQuestionProperties(page: Page) {
  const workspace = page.locator(".named-question-workspace:visible");
  await expect(workspace).toHaveCount(1);
  const properties = workspace.getByRole("button", { name: "Properties", exact: true });
  if (await properties.getAttribute("aria-expanded") !== "true") await properties.click();
  const panel = workspace.getByRole("complementary", { name: "Question properties", exact: true });
  await expect(panel).toBeVisible();
  return panel;
}

export async function closeQuestionProperties(page: Page) {
  const properties = page.locator(".named-question-workspace:visible").getByRole("button", { name: "Properties", exact: true });
  if (await properties.count() && await properties.getAttribute("aria-expanded") === "true") await properties.click();
}

/** Uses the current workspace tabs and section navigation before opening a question control. */
export async function openQuestionSection(page: Page, path: string) {
  const workspace = page.locator(".named-question-workspace:visible");
  await expect(workspace).toHaveCount(1);
  if (path.startsWith("query.")) {
    await closeQuestionProperties(page);
    await selectQuestionTab(page, "build");
  } else if (path === "visualization") {
    await openQuestionProperties(page);
  }
  const section = workspace.locator(`[data-query-path="${path}"]`).first();
  if (path.startsWith("query.")) {
    const id = await section.getAttribute("id");
    expect(id, `Question section ${path}`).toBeTruthy();
    const navigation = workspace.getByRole("navigation", { name: "Question sections", exact: true });
    await navigation.locator(`a[aria-controls="${id}"]`).click();
  }
  await expect(section).toBeVisible();
  const trigger = section.locator("button[aria-expanded]").first();
  if (await trigger.count() && await trigger.getAttribute("aria-expanded") !== "true") await trigger.click();
  return section;
}
